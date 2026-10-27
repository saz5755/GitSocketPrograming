using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 해상 구조정 (RHIB). ManOverboardRescue 가 생성해 익수자에게 보낸다.
///  · 유도: 물과 함께 흘러가는 기준틀에서 계획 — 해류가 배·익수자에 똑같이 작용하므로 상대 위치만으로 접근한다.
///    원거리는 활주 속도로 정렬점까지, 이후 직선 최종 접근 + 제동 곡선 v = √(2·a·d) 로 익수자를 우현 옆에 두고 정지.
///  · 자세: 선체 네 점의 파면 높이로 피치·롤·히브, 속도별 항주 트림(험프 → 활주), 선회 시 안쪽 경사, 감속 시 선수 숙임.
///    활주 중에는 선체가 짧은 파를 건너뛰므로 파도 추종을 줄인다.
///  · 항적: 선수 경로를 물에 실어 기록해 바다 셰이더에 넘기고(V자 발산파·백파 포말), 선수·차인 물보라와 선미 백파 파티클.
/// 모델: Resources/RescueBoat/scene (Sketchfab glTF) 가 있으면 사용하고 길이를 맞춰 배치, 없으면 절차 모델.
/// </summary>
public class RescueBoat : MonoBehaviour
{
    public const string ModelResource = "RescueBoat/scene";

    [Header("선체")]
    public float length = 7.5f;
    public float beam   = 2.6f;
    [Tooltip("수면 아래로 잠기는 깊이 (m) — 정지 시")]
    public float draft  = 0.45f;
    [Tooltip("불러온 모델의 선수 방향 보정 (deg)")]
    public float modelYaw = 0f;
    [Tooltip("선저에서 승조원이 서는 갑판까지 높이 (m)")]
    public float crewDeckHeight = 0.9f;

    [Header("기동")]
    public float maxSpeed   = 17f;     // ≈ 33 kn 활주
    public float accel      = 3.5f;
    public float brake      = 2.2f;    // 스로틀 차단 + 저항 감속
    public float turnRadius = 20f;     // 활주 중 최소 선회 반경
    public float sideOffset = 5.5f;    // 정지 시 익수자까지 측면 거리

    public bool    Arrived  { get; private set; }
    public float   Speed    => _speed;
    public Vector3 Forward  => new Vector3(Mathf.Sin(_heading * Mathf.Deg2Rad), 0f, Mathf.Cos(_heading * Mathf.Deg2Rad));
    public Vector3 Right    => new Vector3(Forward.z, 0f, -Forward.x);
    /// <summary>구명환을 던지는 손 / 줄이 매인 지점</summary>
    public Transform ThrowPoint { get; private set; }
    /// <summary>익수자를 끌어올리는 우현 뱃전 지점 (월드)</summary>
    public Vector3 BoardingPoint => transform.position + Right * (beam * 0.5f + 0.5f) - Forward * 0.8f;
    public Animator CrewAnimator { get; private set; }

    OceanSurface _ocean;
    Vector3 _target;            // 익수자 위치 (월드, 매 프레임 갱신)
    Vector3 _approachDir;       // 최종 접근 방향 (고정)
    float   _heading, _speed, _yawRate, _prevSpeed;
    // 자세 상태
    float _pitch, _roll, _heave, _squat;
    Transform _visual;

    // 항적 기록 (선수 위치, 경과 시간, 속도)
    readonly List<Vector4> _trailPts = new();
    readonly Vector4[] _trailBuf = new Vector4[OceanSurface.BoatTrailMax];
    Vector3 _lastTrailPos;

    ParticleSystem _bowSprayL, _bowSprayR, _sternWash, _mist;

    // ── 생성 ────────────────────────────────────────────────────────────────
    public static RescueBoat Spawn(Vector3 position, Vector3 heading)
    {
        var go = new GameObject("RescueBoat");
        var b = go.AddComponent<RescueBoat>();
        b.Init(position, heading);
        return b;
    }

    void Init(Vector3 position, Vector3 heading)
    {
        _ocean = OceanSurface.Instance;
        heading.y = 0f;
        _heading = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
        _approachDir = heading.normalized;
        _speed = maxSpeed * 0.9f;
        transform.position = new Vector3(position.x, _ocean != null ? _ocean.SampleHeight(position.x, position.z) : position.y, position.z);
        transform.rotation = Quaternion.Euler(0f, _heading, 0f);
        _lastTrailPos = BowPos();

        _visual = new GameObject("Visual").transform;
        _visual.SetParent(transform, false);
        BuildModel();
        BuildCrew();
        BuildSpray();
    }

    /// <summary>익수자 위치와 최종 접근 방향 지정. approachDir 은 접근 시작 시 한 번 정한다.</summary>
    public void SetTarget(Vector3 swimmer) => _target = swimmer;
    public void SetApproachDirection(Vector3 dir) { dir.y = 0f; if (dir.sqrMagnitude > 1e-4f) _approachDir = dir.normalized; }

    Vector3 BowPos() => transform.position + Forward * (length * 0.5f);

    // ── 갱신 ────────────────────────────────────────────────────────────────
    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 water = _ocean != null ? _ocean.WaterVelocity : Vector3.zero;

        Steer(dt);

        // 물과 함께 흘러가는 기준틀에서의 속도 + 해류
        Vector3 vel = Forward * _speed + water;
        Vector3 pos = transform.position + vel * dt;
        transform.position = pos;

        UpdatePose(dt);
        UpdateTrail(dt, water);
        UpdateSpray();
        _prevSpeed = _speed;
    }

    void Steer(float dt)
    {
        Vector3 pos = transform.position; pos.y = 0f;
        Vector3 tgt = _target; tgt.y = 0f;
        Vector3 D = _approachDir;
        Vector3 R = new Vector3(D.z, 0f, -D.x);
        // 정지 자세: 익수자가 우현 옆, 선체 중앙보다 약간 뒤 (던지는 사람 앞)
        Vector3 stop = tgt - R * sideOffset + D * 0.8f;
        float along = Vector3.Dot(stop - pos, D);          // 정지점까지 접근선 방향 남은 거리
        float lateral = Vector3.Dot(pos - stop, R);

        Vector3 aim;
        float cmd;
        bool final = along < 90f && Mathf.Abs(lateral) < 30f && along > -4f;
        if (!final)
        {
            // 정렬점: 접근선 위 정지점 70m 앞
            aim = stop - D * 70f;
            if ((aim - pos).sqrMagnitude < 15f * 15f) aim = stop;
            cmd = maxSpeed;
        }
        else
        {
            // 접근선을 따라 순수 추종 (lookahead 가 짧아질수록 정밀 정렬)
            float look = Mathf.Clamp(along * 0.5f, 3f, 25f);
            aim = stop - D * Mathf.Max(along - look, 0f) + R * 0f;
            if (along < look) aim = stop + D * 2f;
            cmd = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * brake * 0.8f * Mathf.Max(along, 0f)) + 0.2f);
            if (along < 1.2f) cmd = 0f;
        }

        // 도착: 정지점 근처에서 거의 멈추면 위치 유지 (물에 함께 떠내려감)
        float distStop = new Vector2(along, lateral).magnitude;
        if (!Arrived && distStop < 2.0f && _speed < 0.9f) Arrived = true;
        if (Arrived)
        {
            cmd = Mathf.Clamp(along * 0.4f, -0.5f, 0.8f);
            aim = stop + D * 6f;
        }

        // 선회: 활주 중 반경 제한 (ω = v/R), 저속에선 키 효과로 25°/s
        Vector3 to = aim - pos;
        float desired = to.sqrMagnitude > 0.01f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : _heading;
        float err = Mathf.DeltaAngle(_heading, desired);
        float maxRate = Mathf.Min(38f, Mathf.Max(18f, _speed / turnRadius * Mathf.Rad2Deg));
        float wantRate = Mathf.Clamp(err * 1.4f, -maxRate, maxRate);
        _yawRate = Mathf.MoveTowards(_yawRate, wantRate, 60f * dt);
        _heading += _yawRate * dt;

        // 속도: 가속은 엔진, 감속은 스로틀 차단 + 저항 (선회 중엔 저항이 커져 약간 줄어든다)
        float turnDrag = Mathf.Abs(_yawRate) / 38f * 0.15f;
        float target = cmd * (1f - turnDrag);
        _speed = Mathf.MoveTowards(_speed, target, (target > _speed ? accel : brake) * dt);
    }

    void UpdatePose(float dt)
    {
        Vector3 f = Forward, r = Right;
        Vector3 p = transform.position;
        float hb = 0, hs = 0, hp = 0, hst = 0;
        if (_ocean != null)
        {
            Vector3 b = p + f * length * 0.35f, s = p - f * length * 0.35f, pl = p - r * beam * 0.45f, sb = p + r * beam * 0.45f;
            hb = _ocean.SampleHeight(b.x, b.z); hs = _ocean.SampleHeight(s.x, s.z);
            hp = _ocean.SampleHeight(pl.x, pl.z); hst = _ocean.SampleHeight(sb.x, sb.z);
        }
        float v = _speed;
        float planing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 14f, v));
        float hump = Mathf.Exp(-Mathf.Pow((v - 7.5f) / 3f, 2f));
        float follow = Mathf.Lerp(1f, 0.45f, planing);           // 활주 중엔 짧은 파를 건너뛴다

        float wavePitch = Mathf.Atan2(hb - hs, length * 0.7f) * Mathf.Rad2Deg * follow;
        float waveRoll  = Mathf.Atan2(hp - hst, beam * 0.9f) * Mathf.Rad2Deg * follow;
        float surface = (hb + hs + hp + hst) * 0.25f;

        // 항주 트림 (선수 들림): 배수 1° → 험프 ~7° → 활주 ~3°
        float trim = 1f + 6f * hump + 2.2f * planing;
        // 감속 시 선미파가 따라와 선수가 숙여진다
        float decel = (_prevSpeed - _speed) / dt;
        _squat = Mathf.Lerp(_squat, Mathf.Clamp(decel - 0.8f, 0f, 3f) * 1.1f, dt * 2f);
        // 선회 경사: 활주선은 안쪽으로 기운다 (tanφ ∝ v·ω/g)
        float bank = Mathf.Atan(v * _yawRate * Mathf.Deg2Rad / 9.81f) * Mathf.Rad2Deg * Mathf.Lerp(0.25f, 0.7f, planing);
        // 히브: 활주 시 떠오르고 험프에서 가라앉는다
        float lift = 0.14f * planing - 0.08f * hump;

        float k = 1f - Mathf.Exp(-dt * 5f);
        _pitch = Mathf.Lerp(_pitch, -(trim - _squat) - wavePitch, k);
        _roll  = Mathf.Lerp(_roll, -bank + waveRoll, k);
        _heave = Mathf.Lerp(_heave, surface + lift, 1f - Mathf.Exp(-dt * 7f));

        transform.position = new Vector3(p.x, _heave, p.z);
        transform.rotation = Quaternion.Euler(0f, _heading, 0f) * Quaternion.Euler(_pitch, 0f, _roll);
    }

    // 선수 경로를 물에 실어 기록 (점들은 해류로 흘러가고 나이를 먹는다)
    void UpdateTrail(float dt, Vector3 water)
    {
        for (int i = 0; i < _trailPts.Count; i++)
        {
            var t = _trailPts[i];
            _trailPts[i] = new Vector4(t.x + water.x * dt, t.y + water.z * dt, t.z + dt, t.w);
        }
        _lastTrailPos += water * dt;
        Vector3 bow = BowPos();
        if (_trailPts.Count == 0 || (bow - _lastTrailPos).sqrMagnitude > 6f * 6f)
        {
            _trailPts.Insert(0, new Vector4(bow.x, bow.z, 0f, _speed));
            _lastTrailPos = bow;
            if (_trailPts.Count > OceanSurface.BoatTrailMax - 1) _trailPts.RemoveAt(_trailPts.Count - 1);
        }
        if (_ocean == null) return;
        // 0번은 항상 현재 선수
        _trailBuf[0] = new Vector4(bow.x, bow.z, 0f, _speed);
        int n = 1;
        foreach (var t in _trailPts) { if (n >= _trailBuf.Length) break; _trailBuf[n++] = t; }
        _ocean.SetBoatWake(_trailBuf, n, beam, length);
    }

    void OnDestroy()
    {
        if (_ocean != null) _ocean.ClearBoatWake();
    }

    // ── 물보라 ──────────────────────────────────────────────────────────────
    void BuildSpray()
    {
        var mat = (Resources.Load<Material>("VFX/Mat_WaterSpray") ?? Resources.Load<Material>("VFX/Mat_Alpha"));
        _bowSprayL = MakeSpray("BowSprayL", mat, new Vector3(-beam * 0.45f, 0.1f, length * 0.28f), -1f);
        _bowSprayR = MakeSpray("BowSprayR", mat, new Vector3( beam * 0.45f, 0.1f, length * 0.28f),  1f);

        // 선미: 프로펠러가 들어 올린 물방울 (흰 물 자체는 바다 셰이더의 백파 포말이 그린다)
        _sternWash = MakeEmitter("SternWash", mat, new Vector3(0f, 0.0f, -length * 0.5f - 0.3f), 300);
        var m = _sternWash.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.32f);
        m.startColor = new Color(0.97f, 0.99f, 1f, 0.85f);
        m.gravityModifier = 1f;
        var sh = _sternWash.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 30f; sh.radius = beam * 0.25f;
        sh.rotation = new Vector3(-50f, 180f, 0f);   // 위·뒤로
        var sz = _sternWash.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(1, 1.5f)));

        // 물안개: 활주 중 선체 주변에 남는 옅은 비말
        _mist = MakeEmitter("Mist", mat, new Vector3(0f, 0.3f, -length * 0.2f), 200);
        var mm = _mist.main;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        mm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.0f);
        mm.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
        mm.startColor = new Color(0.96f, 0.98f, 1f, 0.10f);
        mm.gravityModifier = -0.02f;
        var msh = _mist.shape; msh.shapeType = ParticleSystemShapeType.Box; msh.scale = new Vector3(beam, 0.3f, length * 0.8f);
    }

    // 차인(선저 모서리)에서 위·바깥·뒤로 뿜어지는 물보라 판 — 속도²에 비례
    ParticleSystem MakeSpray(string name, Material mat, Vector3 local, float side)
    {
        var ps = MakeEmitter(name, mat, local, 400);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
        m.startColor = new Color(0.96f, 0.98f, 1f, 0.75f);
        m.gravityModifier = 1f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 0.15f;
        // 바깥(side)·위·뒤로
        sh.rotation = new Vector3(-35f, 180f - side * 55f, 0f);
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(1, 2.2f)));
        return ps;
    }

    ParticleSystem MakeEmitter(string name, Material mat, Vector3 local, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.maxParticles = max;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.playOnAwake = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (mat != null) r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
        return ps;
    }

    void UpdateSpray()
    {
        float v = _speed;
        float spray = Mathf.Max(0f, v - 4f);
        SetRate(_bowSprayL, spray * spray * 1.4f);
        SetRate(_bowSprayR, spray * spray * 1.4f);
        // 스로틀(가속·고속 유지)에 비례한 백파, 정지 중에도 아이들 백파 약간
        float throttle = Mathf.Clamp01(v / maxSpeed) + Mathf.Max(0f, (_speed - _prevSpeed) / Mathf.Max(Time.deltaTime, 1e-3f)) * 0.1f;
        SetRate(_sternWash, 4f + throttle * 90f);
        SetRate(_mist, Mathf.InverseLerp(9f, 16f, v) * 25f);
    }

    static void SetRate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var e = ps.emission; e.rateOverTime = rate;
    }

    // ── 모델 ────────────────────────────────────────────────────────────────
    void BuildModel()
    {
        var prefab = Resources.Load<GameObject>(ModelResource);
        if (prefab != null)
        {
            var m = Instantiate(prefab, _visual, false);
            m.name = "Model";
            FitModel(m.transform);
        }
        else BuildProceduralRhib(_visual);

        ThrowPoint = new GameObject("ThrowPoint").transform;
        ThrowPoint.SetParent(_visual, false);
        ThrowPoint.localPosition = new Vector3(beam * 0.35f, -draft + crewDeckHeight + 1.35f, -length * 0.1f);   // 승조원 손 높이
    }

    // 모델 길이를 선장에 맞추고, 중심을 원점에, 선저를 흘수 아래로
    void FitModel(Transform m)
    {
        m.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
        m.localPosition = Vector3.zero;
        m.localScale = Vector3.one;
        foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
        var rends = m.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = LocalBounds(rends, _visual);
        float longest = Mathf.Max(b.size.x, b.size.z);
        if (longest < 1e-3f) return;
        float s = length / longest;
        m.localScale = Vector3.one * s;
        b = LocalBounds(rends, _visual);
        m.localPosition = new Vector3(-b.center.x, -b.min.y - draft, -b.center.z);
    }

    static Bounds LocalBounds(Renderer[] rends, Transform space)
    {
        bool init = false; Bounds b = default;
        foreach (var r in rends)
        {
            var wb = r.bounds;
            Vector3 c = wb.center, e = wb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 l = space.InverseTransformPoint(corner);
                if (!init) { b = new Bounds(l, Vector3.zero); init = true; } else b.Encapsulate(l);
            }
        }
        return b;
    }

    // 승조원: 우현 조타석 옆에 서서 익수자를 바라본다 (Soldier 모델, 지정이 없으면 지상 캐릭터 모델)
    void BuildCrew()
    {
        var prefab = PlayerManager.SharedRescueCrewPrefab != null ? PlayerManager.SharedRescueCrewPrefab : PlayerManager.SharedGroundCharPrefab;
        GameObject crew;
        if (prefab != null)
        {
            crew = Instantiate(prefab);
            // 조작·네트워크 스크립트 제거 (Animator 는 MonoBehaviour 가 아니라 남는다)
            foreach (var mb in crew.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var c in crew.GetComponentsInChildren<Collider>()) Destroy(c);
            var cc = crew.GetComponent<CharacterController>(); if (cc != null) Destroy(cc);
            CrewAnimator = crew.GetComponentInChildren<Animator>();
            if (CrewAnimator != null) CrewAnimator.applyRootMotion = false;
        }
        else
        {
            crew = new GameObject("Crew");
            crew.AddComponent<CharacterModelBuilder>();
        }
        crew.name = "Crew";
        crew.transform.SetParent(_visual, false);
        crew.transform.localPosition = new Vector3(beam * 0.22f, -draft + crewDeckHeight, -length * 0.1f);
        crew.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // 우현(익수자 쪽)을 향함
    }

    // ── 절차 RHIB (모델 파일이 없을 때) ─────────────────────────────────────
    // 단단한 V 선저 + 위를 두른 고무 튜브(칼라) + 갑판 + 콘솔 + T-탑 + 선외기 2기
    void BuildProceduralRhib(Transform parent)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        Material Mat(Color c, float smooth) { var m = new Material(lit); m.color = c; m.SetFloat("_Smoothness", smooth); return m; }
        var hullMat = Mat(new Color(0.78f, 0.80f, 0.82f), 0.55f);
        var tubeMat = Mat(new Color(0.85f, 0.22f, 0.08f), 0.35f);
        var deckMat = Mat(new Color(0.16f, 0.17f, 0.18f), 0.2f);
        var darkMat = Mat(new Color(0.06f, 0.06f, 0.07f), 0.6f);

        float L = length, B = beam, half = L * 0.5f;
        // 실제 RHIB 비율: 흘수 ≈ 0.45m, 고무 튜브 윗면이 수면 위 ≈ 0.8m (건현이 낮으면 파도가 갑판을 덮는다)
        float bottom = -draft, chineY = bottom + 0.3f, sheerY = bottom + 0.95f;

        // V 선저: 단면 (용골, 차인 좌우, 현측 상단 좌우) 을 길이 방향으로 이어 붙이고 선수에서 모은다
        var hull = new Mesh { name = "RhibHull" };
        var verts = new List<Vector3>(); var tris = new List<int>();
        const int Seg = 16;
        for (int i = 0; i <= Seg; i++)
        {
            float t = i / (float)Seg;               // 0 = 선미, 1 = 선수
            float z = Mathf.Lerp(-half, half * 0.98f, t);
            float w = (B * 0.42f) * (t < 0.6f ? 1f : Mathf.Cos((t - 0.6f) / 0.4f * Mathf.PI * 0.5f) * 0.95f + 0.05f);
            float keelRise = t < 0.6f ? 0f : Mathf.Pow((t - 0.6f) / 0.4f, 2f) * 0.45f;   // 선수 쪽 용골이 올라간다
            verts.Add(new Vector3(0f, bottom + keelRise, z));
            verts.Add(new Vector3(-w, chineY + keelRise * 0.5f, z));
            verts.Add(new Vector3(w, chineY + keelRise * 0.5f, z));
            verts.Add(new Vector3(-w * 1.02f, sheerY, z));
            verts.Add(new Vector3(w * 1.02f, sheerY, z));
        }
        for (int i = 0; i < Seg; i++)
        {
            int a = i * 5, b = (i + 1) * 5;
            void Q(int p0, int p1, int p2, int p3) { tris.Add(p0); tris.Add(p1); tris.Add(p2); tris.Add(p0); tris.Add(p2); tris.Add(p3); }
            Q(a + 0, b + 0, b + 1, a + 1); Q(a + 0, a + 2, b + 2, b + 0);
            Q(a + 1, b + 1, b + 3, a + 3); Q(a + 2, a + 4, b + 4, b + 2);
        }
        // 트랜섬
        tris.AddRange(new[] { 0, 1, 3, 0, 3, 4, 0, 4, 2 });
        hull.SetVertices(verts); hull.SetTriangles(tris, 0); hull.RecalculateNormals(); hull.RecalculateBounds();
        AddMesh(parent, "Hull", hull, hullMat);

        // 고무 튜브: U자 경로(양현 직선 + 선수 반원)를 따라 원 단면을 쓸어 만든다
        var path = new List<Vector3>();
        float tubeR = 0.27f, tw = B * 0.5f - tubeR, tz0 = -half - 0.1f, tz1 = half - tw * 0.9f;
        for (int i = 0; i <= 6; i++) path.Add(new Vector3(-tw, sheerY + tubeR * 0.6f, Mathf.Lerp(tz0, tz1, i / 6f)));
        for (int i = 1; i < 12; i++)
        {
            float a = Mathf.PI - i / 12f * Mathf.PI;
            path.Add(new Vector3(Mathf.Cos(a) * tw, sheerY + tubeR * 0.6f + Mathf.Sin(a) * 0.12f, tz1 + Mathf.Sin(a) * tw * 0.9f));
        }
        for (int i = 0; i <= 6; i++) path.Add(new Vector3(tw, sheerY + tubeR * 0.6f, Mathf.Lerp(tz1, tz0, i / 6f)));
        AddMesh(parent, "Tube", SweepTube(path, tubeR, 14), tubeMat);

        // 갑판 · 콘솔 · 좌석 · T-탑 · 선외기
        Box(parent, "Deck", new Vector3(0f, sheerY - 0.05f, -0.3f), new Vector3(B * 0.75f, 0.06f, L * 0.8f), deckMat);
        Box(parent, "Console", new Vector3(0f, sheerY + 0.45f, 0.2f), new Vector3(0.75f, 0.9f, 0.7f), Mat(new Color(0.9f, 0.9f, 0.88f), 0.5f));
        Box(parent, "Windscreen", new Vector3(0f, sheerY + 1.05f, 0.5f), new Vector3(0.8f, 0.35f, 0.05f), Mat(new Color(0.2f, 0.25f, 0.3f, 1f), 0.95f));
        Box(parent, "Seat", new Vector3(0f, sheerY + 0.35f, -0.9f), new Vector3(0.9f, 0.7f, 0.6f), darkMat);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(parent, "ArchPost", new Vector3(s * 0.55f, sheerY + 1.0f, -0.5f), new Vector3(0.06f, 2.0f, 0.06f), darkMat);
            Box(parent, "Outboard", new Vector3(s * 0.45f, sheerY + 0.25f, -half - 0.35f), new Vector3(0.45f, 0.75f, 0.55f), darkMat);
            Box(parent, "Leg", new Vector3(s * 0.45f, bottom + 0.1f, -half - 0.35f), new Vector3(0.12f, 0.6f, 0.2f), darkMat);
        }
        Box(parent, "ArchTop", new Vector3(0f, sheerY + 2.0f, -0.5f), new Vector3(1.2f, 0.06f, 0.5f), darkMat);
    }

    static void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Mesh SweepTube(List<Vector3> path, float radius, int sides)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 fwd = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, side);
            for (int s = 0; s < sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f;
                Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                v.Add(path[i] + dir * radius); n.Add(dir);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = i * sides + s, b = i * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
            }
        var m = new Mesh { name = "RhibTube" };
        m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
        return m;
    }
}
