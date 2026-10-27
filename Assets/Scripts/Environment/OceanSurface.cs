using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 바다 표면. 씬의 빈 GameObject 에 부착하고 Inspector 에서 항모를 지정한다.
///  · [ExecuteAlways] — 플레이하지 않아도 씬/게임 뷰에 그려지고 Inspector 값이 즉시 반영된다
///  · 카메라 기준 극좌표 격자 메쉬(가까울수록 촘촘) + Custom/Ocean 셰이더. 격자 위치는 셰이더가 카메라마다 계산하므로
///    게임 오브젝트를 만들지 않고 Graphics.RenderMesh 로 각 카메라에 그린다 (하이어라키·씬 저장에 흔적 없음)
///  · 항모는 게임플레이상 정지해 있지만, 물결이 함정 진행 반대 방향으로 흘러 항해하는 것처럼 보이고
///    선미 뒤로 Kelvin V자 항적이 생긴다 (virtualSpeed)
///  · 플레이 중에만: 카메라 원거리 클립 확장, 스카이박스 지면색을 연무색으로 (복제본만 수정 — 에셋/씬은 그대로)
/// 충돌체는 만들지 않는다 (게임플레이 영향 없음).
/// </summary>
[ExecuteAlways]
public class OceanSurface : MonoBehaviour
{
    [Header("수면")]
    [Tooltip("해수면 높이 (월드 Y). 항모 흘수선에 맞춤")]
    public float seaLevel = -11f;
    [Tooltip("너울 높이 배율 (1 = 보통 해상, 0.5 = 잔잔, 2 = 거친 바다)")]
    [Range(0f, 3f)] public float waveScale = 1f;
    [Tooltip("파도 마루의 뾰족함 (0 = 둥근 사인파, 1 = 기본, 2 = 날카로운 마루)")]
    [Range(0f, 2f)] public float choppiness = 1f;
    [Tooltip("바람 잔물결 세기")]
    [Range(0f, 2f)] public float rippleStrength = 1f;
    [Tooltip("수백 m 단위로 거칠고 잔잔한 해면이 얼룩지는 정도")]
    [Range(0f, 1f)] public float windPatches = 0.6f;
    [Tooltip("파도 마루 흰 포말 양")]
    [Range(0f, 2f)] public float foamAmount = 1f;

    [Header("색")]
    public Color deepColor = new Color(0.012f, 0.045f, 0.085f);
    [Tooltip("파도 마루를 투과한 햇빛 색")]
    public Color crestScatterColor = new Color(0.030f, 0.165f, 0.190f);
    public Color foamColor = new Color(0.92f, 0.95f, 0.97f);
    [Tooltip("수면에 반사되는 하늘 천정 색")]
    public Color skyZenithColor = new Color(0.24f, 0.42f, 0.68f);

    [Header("항모 흘수선 (컴포넌트 우클릭 → '흘수선 자동 측정')")]
    public Transform carrier;
    [Tooltip("항모 로컬 기준 진행 방향 (선수 쪽)")]
    public Vector3 forwardLocal = Vector3.back;
    [Tooltip("항모 로컬 기준 흘수선 중심 (선체가 피벗에서 좌우로 치우친 만큼 x)")]
    public Vector3 hullCenterLocal = new Vector3(-4.66f, 0f, 0f);
    [Tooltip("흘수선 반길이 (m) — 선수 끝~선미 끝의 절반")]
    public float hullHalfLength = 87f;
    [Tooltip("흘수선 최대 반폭 (m)")]
    public float hullHalfBeam = 11.77f;
    [Tooltip("선수 끝에서 최대 폭이 될 때까지 길이 (m) — 선수 쐐기 입사각을 정한다")]
    public float bowEntryLength = 60f;
    [Tooltip("선미 끝에서 최대 폭이 될 때까지 길이 (m)")]
    public float sternRunLength = 55f;
    [Tooltip("흘수 (m) — 짧은 발산파의 감쇠 깊이")]
    public float draft = 8.6f;

    [Header("항적 (물리 근거)")]
    [Tooltip("연출용 항해 속도 (m/s) — 물결 흐름, Kelvin 파장(λ = 2πU²/g), 선수파 높이(∝ U²/2g)에 사용. 실제 항모 위치는 움직이지 않는다")]
    [Range(0f, 25f)] public float virtualSpeed = 12f;
    [Tooltip("항적이 그려지는 최대 거리 (m). 끝에서 부드럽게 사라진다")]
    public float wakeExtent = 2500f;
    [Tooltip("항적 물결·포말 전체 세기")]
    [Range(0f, 2f)] public float wakeStrength = 1f;
    [Tooltip("Kelvin 파 기준 높이 (m) — 선체 길이 거리에서의 진폭")]
    [Range(0f, 2f)] public float kelvinHeight = 1.2f;
    [Tooltip("선수파 높이 = 이 값 × U²/2g (정체점 수두 대비 비율)")]
    [Range(0f, 0.6f)] public float bowWave = 0.3f;
    [Tooltip("표면 포말 수명 (초) — 물이 흘러가며 레이스·패치로 흩어진다")]
    public float foamLifetime = 35f;
    [Tooltip("수중 기포 수명 (초) — 청록빛 산란")]
    public float bubbleLifetime = 45f;
    [Tooltip("슬릭 수명 (초) — 잔물결이 눌린 매끈한 띠")]
    public float slickLifetime = 400f;
    [Tooltip("기포가 섞인 물빛")]
    public Color aeratedColor = new Color(0.05f, 0.24f, 0.24f);

    [Header("원경")]
    [Tooltip("스카이박스의 실제 수평선 색을 측정해 연무색으로 사용 (바다·구름이 하늘과 이음새 없이 이어짐). 끄면 아래 hazeColor 사용")]
    public bool autoHazeFromSky = true;
    public Color hazeColor = new Color(0.66f, 0.75f, 0.82f);
    public float hazeStart = 600f;
    public float hazeEnd = 14000f;
    [Tooltip("플레이 중 메인 카메라 원거리 클립 (바다가 수평선까지 이어지도록 확장)")]
    public float cameraFar = 20000f;

    /// <summary>다른 환경 요소(구름)가 같은 연무색을 쓰도록 공유</summary>
    public static Color? SharedHaze;

    // 링 간격(반지름 비례)과 원주 간격이 비슷하도록 → 삼각형이 정사각에 가까워 어느 방향으로도 고르게 표현
    const int Rings = 400, Segments = 256;
    const float InnerRadius = 0.6f, OuterRadius = 18000f;
    static readonly float GridK = Mathf.Max(Mathf.Log(OuterRadius / InnerRadius) / Rings, 2f * Mathf.PI / Segments);

    static Mesh s_mesh;
    Material _mat;
    Material _skyClone, _skyOriginal;
    Color _haze;
    bool _needSample = true;

    static readonly int ID_HullCenter = Shader.PropertyToID("_HullCenter");
    static readonly int ID_ShipFwd = Shader.PropertyToID("_ShipFwd");
    static readonly int ID_ShipSpeed = Shader.PropertyToID("_ShipSpeed");
    static readonly int ID_HullShape = Shader.PropertyToID("_HullShape");
    static readonly int ID_Draft = Shader.PropertyToID("_Draft");
    static readonly int ID_WakeLength = Shader.PropertyToID("_WakeLength");
    static readonly int ID_WakeStrength = Shader.PropertyToID("_WakeStrength");
    static readonly int ID_KelvinHeight = Shader.PropertyToID("_KelvinHeight");
    static readonly int ID_BowWave = Shader.PropertyToID("_BowWave");
    static readonly int ID_WakeLife = Shader.PropertyToID("_WakeLife");
    static readonly int ID_Aerated = Shader.PropertyToID("_AeratedColor");
    static readonly int ID_Haze = Shader.PropertyToID("_HazeColor");
    static readonly int ID_HazeStart = Shader.PropertyToID("_HazeStart");
    static readonly int ID_HazeEnd = Shader.PropertyToID("_HazeEnd");
    static readonly int ID_WaveScale = Shader.PropertyToID("_WaveScale");
    static readonly int ID_Choppiness = Shader.PropertyToID("_Choppiness");
    static readonly int ID_Ripple = Shader.PropertyToID("_RippleStrength");
    static readonly int ID_Patches = Shader.PropertyToID("_WindPatches");
    static readonly int ID_FoamAmount = Shader.PropertyToID("_FoamAmount");
    static readonly int ID_Deep = Shader.PropertyToID("_DeepColor");
    static readonly int ID_Shallow = Shader.PropertyToID("_ShallowColor");
    static readonly int ID_Foam = Shader.PropertyToID("_FoamColor");
    static readonly int ID_Zenith = Shader.PropertyToID("_ZenithColor");
    static readonly int ID_SeaLevel = Shader.PropertyToID("_SeaLevel");
    static readonly int ID_GridK = Shader.PropertyToID("_GridK");
    static readonly int ID_Outer = Shader.PropertyToID("_OuterRadius");

    // ── CPU 수면 높이 (셰이더 너울과 같은 식) ────────────────────────────────
    // 수영하는 캐릭터·구조정·구명환이 실제 렌더된 파도 위에 뜨도록. Ocean.shader 의 SWELL 목록·Env·Gerstner·FlowXZ 와 일치해야 한다.
    public static OceanSurface Instance { get; private set; }

    static readonly (float dx, float dz, float L, float Q, float A)[] Swell =
    {
        ( 1.0f,  0.31f, 48.0f, 0.22f, 0.42f),
        ( 0.83f, -0.57f, 31.0f, 0.20f, 0.27f),
        ( 0.27f,  1.0f, 19.5f, 0.20f, 0.16f),
        (-0.61f,  0.79f, 12.7f, 0.18f, 0.09f),
        ( 0.97f, -0.13f,  8.3f, 0.16f, 0.05f),
        (-0.23f, -0.97f,  5.9f, 0.14f, 0.03f),
    };

    /// <summary>해수 흐름 속도 (월드). 항모는 정지해 있고 물이 반대로 흘러 항해를 표현하므로, 물에 뜬 것은 이 속도로 떠내려간다.</summary>
    public Vector3 WaterVelocity
    {
        get
        {
            Vector3 f = ShipForwardFlat(out float spd);
            return -f * spd;
        }
    }

    Vector3 ShipForwardFlat(out float speed)
    {
        if (carrier == null) { speed = 0.5f; return Vector3.back; }
        Vector3 f = carrier.TransformDirection(forwardLocal); f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        speed = Mathf.Max(0.5f, virtualSpeed);
        return f.normalized;
    }

    /// <summary>파도 시계. 셰이더(_OceanTime)와 CPU 높이 계산이 반드시 같은 값을 쓴다.</summary>
    public static float OceanTime => Application.isPlaying ? Time.timeSinceLevelLoad : (float)Time.realtimeSinceStartupAsDouble;

    /// <summary>
    /// 월드 xz 위치의 수면 높이 (너울 + 항모 항적 — 잔물결·구조정 항적 제외). 셰이더의 정점 변위를 역으로 풀어 해당 지점 높이를 구한다.
    /// 항적(Kelvin 파·선체 물결)은 최대 1m 넘게 솟으므로 빼면 항모 옆·뒤에서 떠 있는 것들이 물에 잠기거나 떠 보인다.
    /// </summary>
    public float SampleHeight(float x, float z)
    {
        float t = OceanTime;
        Vector3 f = ShipForwardFlat(out float spd);
        float fx = f.x * spd * t, fz = f.z * spd * t;
        // 격자점 g 가 (g + 수평변위) 로 옮겨지므로 g 를 고정점 반복으로 찾는다
        float gx = x, gz = z;
        Vector3 d = default;
        for (int it = 0; it < 4; it++)
        {
            d = SwellDisp(gx + fx, gz + fz, t);
            gx = x - d.x; gz = z - d.z;
        }
        d = SwellDisp(gx + fx, gz + fz, t);
        // 셰이더도 항적 높이를 변위 전 격자점에서 계산해 수직으로만 더한다
        return seaLevel + d.y + WakeHeight(gx, gz);
    }

    // ── 항적 높이 (Ocean.shader 의 Wake() 정점 경로를 그대로 옮김 — 셰이더를 고치면 함께 고친다) ──
    //  근거리 기준이라 파장 필터(WF)·원거리 LOD 는 1 로 둔다. 슬릭 감쇠의 가장자리 굴곡 노이즈는 생략.
    float WakeHeight(float x, float z)
    {
        if (carrier == null || wakeStrength <= 0f) return 0f;
        Vector3 c = carrier.TransformPoint(hullCenterLocal);
        Vector3 f = ShipForwardFlat(out float U);
        float rx = x - c.x, rz = z - c.z;
        float a = rx * f.x + rz * f.z;
        float y = rx * f.z - rz * f.x;
        float Hl = hullHalfLength, WL = Mathf.Max(100f, wakeExtent);
        if (a > Hl + 40f || a < -Hl - WL || Mathf.Abs(y) > 80f + Mathf.Max(Hl - a, 0f) * 0.42f) return 0f;

        float hk = KelvinSource(Hl - 4f - a, y, kelvinHeight, U, WL)
                 + KelvinSource(-Hl * 0.6f - a, y, -kelvinHeight * 0.6f, U, WL);
        float hh = HullWave(a, y, U);

        float xs = -Hl - a;
        float hw = hullHalfBeam * 0.8f * Mathf.Pow(1f + Mathf.Max(xs, 0f) / (4f * hullHalfBeam), 1f / 3f);
        float span = SStep(-6f, 6f, xs) * SStep(WL, WL * 0.6f, xs);
        float yw = Mathf.Abs(y) / hw / 1.4f;
        float slick = Mathf.Exp(-yw * yw * yw * yw) * span * Mathf.Exp(-Mathf.Max(xs, 0f) / U / Mathf.Max(1f, slickLifetime));
        return (hk * (1f - 0.5f * slick) + hh) * wakeStrength;
    }

    // HLSL smoothstep (e0 > e1 도 허용)
    static float SStep(float e0, float e1, float v) { float t = Mathf.Clamp01((v - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

    float KelvinSource(float x, float y, float H, float U, float WL)
    {
        if (x <= 0.5f) return 0f;
        float K0 = 9.81f / (U * U);
        float ay = Mathf.Abs(y);
        float r = Mathf.Sqrt(x * x + ay * ay);
        float kr = K0 * r + 1f;
        float tau = ay / x;
        float disc = 1f - 8f * tau * tau;
        float q0 = Mathf.Pow(kr, -1f / 3f);
        float q = Mathf.Sqrt(Mathf.Max(disc, 0f));
        float caustic = Mathf.Sqrt(1f + q0) / Mathf.Sqrt(q + q0);
        float beyond = Mathf.Exp(-Mathf.Max(-disc, 0f) * Mathf.Pow(kr, 2f / 3f) * 2f);
        float amp = H / Mathf.Sqrt(1f + r / (2f * hullHalfLength)) * caustic * beyond * SStep(0f, 25f, x) * Mathf.Exp(-x / WL * 1.5f);
        float tT = disc >= 0f ? 2f * tau / (1f + q) : 0.70710678f;
        float tD = disc >= 0f ? (1f + q) / Mathf.Max(4f * tau, 1e-4f) : 0.70710678f;
        return KelvinTerm(tT, x, ay, K0, amp) + KelvinTerm(tD, x, ay, K0, amp * 0.85f);
    }

    float KelvinTerm(float t, float x, float ay, float K0, float amp)
    {
        float s1 = Mathf.Sqrt(1f + t * t);
        float k = K0 * (1f + t * t);
        return amp * Mathf.Exp(-0.35f * k * draft) * Mathf.Cos(K0 * (x + ay * t) * s1);
    }

    float HullHalfBeamAt(float a)
    {
        float sb = Mathf.Clamp01((hullHalfLength - a) / Mathf.Max(1f, bowEntryLength));
        float ss = Mathf.Clamp01((a + hullHalfLength) / Mathf.Max(1f, sternRunLength));
        return hullHalfBeam * Mathf.Min(Mathf.Pow(sb, 0.95f), Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(ss)));
    }

    float HullDist(float a, float y)
    {
        float ay = Mathf.Abs(y), Hl = hullHalfLength;
        if (a > Hl) return new Vector2(a - Hl, ay).magnitude;
        if (a < -Hl) return new Vector2(-Hl - a, Mathf.Max(ay - hullHalfBeam * 0.3f, 0f)).magnitude;
        return ay - HullHalfBeamAt(a);
    }

    float HullWave(float a, float y, float U)
    {
        float d = HullDist(a, y);
        float xb = hullHalfLength - a, xbp = Mathf.Max(xb, 0f);
        float decay = Mathf.Exp(-xbp / 38f) * SStep(-2f, 4f, xb);
        float Hb = bowWave * U * U / (2f * 9.81f) * decay;
        float tanEntry = hullHalfBeam / Mathf.Max(1f, bowEntryLength);
        float dc = xbp * Mathf.Max(0.445f - tanEntry, 0.05f);
        float wc = 2f + 0.07f * xbp;
        float crest = Mathf.Exp(-((d - dc) / wc) * ((d - dc) / wc));
        float plateau = SStep(dc + wc, dc - wc, d) * 0.55f;
        float K0 = 9.81f / (U * U);
        float side = Mathf.Cos(K0 * xbp) * Mathf.Exp(-Mathf.Max(d, 0f) / 9f)
                   * (Mathf.Abs(a) <= hullHalfLength + 2f ? 1f : 0f) * SStep(0f, 12f, xb);
        float h = Hb * Mathf.Max(crest, plateau) + side * kelvinHeight * 0.9f;
        return h * SStep(-2.5f, 0f, d);
    }

    Vector3 SwellDisp(float px, float pz, float t)
    {
        Vector3 disp = default;
        foreach (var w in Swell)
        {
            float len = Mathf.Sqrt(w.dx * w.dx + w.dz * w.dz);
            float dx = w.dx / len, dz = w.dz / len;
            float A = w.A * waveScale * (0.4f + 1.2f * VNoise(px / (w.L * 5.3f) + w.L * 1.37f, pz / (w.L * 5.3f) + w.L * 1.37f));
            float Q = w.Q * choppiness;
            float k = 2f * Mathf.PI / w.L;
            float c = Mathf.Sqrt(9.81f / k);
            float ph = k * (dx * px + dz * pz - c * t);
            float s = Mathf.Sin(ph), co = Mathf.Cos(ph);
            float qa = Q / k;
            disp.x += dx * qa * co; disp.y += A * s; disp.z += dz * qa * co;
        }
        return disp;
    }

    static float Frac(float v) => v - Mathf.Floor(v);
    static float Hash12(float x, float y)
    {
        float a = Frac(x * 0.1031f), b = Frac(y * 0.1031f), c = Frac(x * 0.1031f);
        float dot = a * (b + 33.33f) + b * (c + 33.33f) + c * (a + 33.33f);
        a += dot; b += dot; c += dot;
        return Frac((a + b) * c);
    }
    static float VNoise(float x, float y)
    {
        float ix = Mathf.Floor(x), iy = Mathf.Floor(y), fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        float a = Hash12(ix, iy), b = Hash12(ix + 1f, iy), c = Hash12(ix, iy + 1f), d = Hash12(ix + 1f, iy + 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    // ── 소형정 항적 (구조정 등) ──────────────────────────────────────────────
    // 항적은 배가 지나간 실제 경로를 따라 그린다: 최근 위치 기록(물에 실려 흘러감)을 셰이더에 넘긴다.
    public const int BoatTrailMax = 24;
    readonly Vector4[] _boatTrail = new Vector4[BoatTrailMax];
    int   _boatTrailCount;
    float _boatBeam = 2.6f, _boatLength = 7.5f;
    Vector4 _boatBounds;
    static readonly int ID_OceanTime = Shader.PropertyToID("_OceanTime");
    static readonly int ID_BoatTrail = Shader.PropertyToID("_BoatTrail");
    static readonly int ID_BoatParams = Shader.PropertyToID("_BoatParams");
    static readonly int ID_BoatBounds = Shader.PropertyToID("_BoatBounds");

    /// <summary>trail[i] = (x, z, 그 지점을 지난 뒤 경과 시간 s, 그때 속도 m/s), i = 0 이 현재 선미. count ≤ BoatTrailMax</summary>
    public void SetBoatWake(Vector4[] trail, int count, float beam, float length)
    {
        _boatTrailCount = Mathf.Clamp(count, 0, BoatTrailMax);
        _boatBeam = beam; _boatLength = length;
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < _boatTrailCount; i++)
        {
            _boatTrail[i] = trail[i];
            minX = Mathf.Min(minX, trail[i].x); maxX = Mathf.Max(maxX, trail[i].x);
            minZ = Mathf.Min(minZ, trail[i].y); maxZ = Mathf.Max(maxZ, trail[i].y);
        }
        for (int i = _boatTrailCount; i < BoatTrailMax; i++) _boatTrail[i] = _boatTrailCount > 0 ? _boatTrail[_boatTrailCount - 1] : Vector4.zero;
        // V자 팔이 경로 밖으로 벌어지므로 여유를 둔다
        const float m = 60f;
        _boatBounds = _boatTrailCount > 0 ? new Vector4(minX - m, minZ - m, maxX + m, maxZ + m) : Vector4.zero;
    }

    public void ClearBoatWake() => _boatTrailCount = 0;

    void OnEnable()
    {
        Instance = this;
        var sh = Shader.Find("Custom/Ocean");
        if (sh == null) { Debug.LogWarning("[OceanSurface] Custom/Ocean 셰이더 없음"); return; }
        _mat = new Material(sh) { name = "Ocean (runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (s_mesh == null) s_mesh = BuildPolarGrid();
        _haze = hazeColor;
        SharedHaze = _haze;
        _needSample = true;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (_skyClone != null)
        {
            if (RenderSettings.skybox == _skyClone) RenderSettings.skybox = _skyOriginal;
            DestroySafe(_skyClone);
            _skyClone = null;
        }
        DestroySafe(_mat);
        _mat = null;
    }

    void OnValidate() => _needSample = true;

    void Update()
    {
        if (_mat == null) return;
        if (_needSample)
        {
            _needSample = false;
            _haze = autoHazeFromSky ? SampleSkyHorizon(hazeColor) : hazeColor;
            SharedHaze = _haze;
            if (Application.isPlaying) ApplySkyGround();
        }
        if (Application.isPlaying)
        {
            var cam = Camera.main;
            if (cam != null && cam.farClipPlane < cameraFar) cam.farClipPlane = cameraFar;
        }
    }

    // 스카이박스 지면색도 연무색으로 (스커트 틈새로 혹시 보이더라도 튀지 않게). 플레이 중 복제본에만 적용
    void ApplySkyGround()
    {
        var sky = _skyClone != null ? _skyOriginal : RenderSettings.skybox;
        if (sky == null || !sky.HasProperty("_GroundColor")) return;
        if (_skyClone == null)
        {
            _skyOriginal = sky;
            _skyClone = new Material(sky) { name = sky.name + " (ocean)", hideFlags = HideFlags.HideAndDontSave };
            RenderSettings.skybox = _skyClone;
        }
        _skyClone.SetColor("_GroundColor", _haze);
    }

    static bool ShouldRender(Camera cam)
    {
        if (cam.cameraType == CameraType.SceneView) return true;
        return cam.cameraType == CameraType.Game && cam == Camera.main;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_mat == null || s_mesh == null || !ShouldRender(cam)) return;
        ApplyMaterial();
        var rp = new RenderParams(_mat)
        {
            camera = cam,
            layer = gameObject.layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            worldBounds = new Bounds(cam.transform.position, Vector3.one * (OuterRadius * 2.2f)),
        };
        Graphics.RenderMesh(rp, s_mesh, 0, Matrix4x4.identity);
    }

    void ApplyMaterial()
    {
        if (carrier != null)
        {
            Vector3 center = carrier.TransformPoint(hullCenterLocal);
            Vector3 fwd = carrier.TransformDirection(forwardLocal); fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            _mat.SetVector(ID_HullCenter, new Vector4(center.x, center.y, center.z, 0f));
            _mat.SetVector(ID_ShipFwd, new Vector4(fwd.x, 0f, fwd.z, 0f));
            _mat.SetFloat(ID_ShipSpeed, Mathf.Max(0.5f, virtualSpeed));
            _mat.SetFloat(ID_WakeStrength, wakeStrength);
        }
        else
        {
            _mat.SetVector(ID_HullCenter, new Vector4(0f, 0f, 1e6f, 0f));   // 항적 없음
            _mat.SetVector(ID_ShipFwd, new Vector4(0f, 0f, -1f, 0f));
            _mat.SetFloat(ID_ShipSpeed, 0.5f);
            _mat.SetFloat(ID_WakeStrength, 0f);
        }
        _mat.SetVector(ID_HullShape, new Vector4(hullHalfLength, hullHalfBeam, Mathf.Max(1f, bowEntryLength), Mathf.Max(1f, sternRunLength)));
        _mat.SetFloat(ID_Draft, draft);
        _mat.SetFloat(ID_WakeLength, Mathf.Max(100f, wakeExtent));
        _mat.SetFloat(ID_KelvinHeight, kelvinHeight);
        _mat.SetFloat(ID_BowWave, bowWave);
        _mat.SetVector(ID_WakeLife, new Vector4(Mathf.Max(1f, foamLifetime), Mathf.Max(1f, bubbleLifetime), Mathf.Max(1f, slickLifetime), 0f));
        _mat.SetColor(ID_Aerated, aeratedColor);
        _mat.SetFloat(ID_OceanTime, OceanTime);
        _mat.SetVectorArray(ID_BoatTrail, _boatTrail);
        _mat.SetVector(ID_BoatParams, new Vector4(_boatTrailCount, _boatBeam, _boatLength, _boatTrailCount >= 2 ? 1f : 0f));
        _mat.SetVector(ID_BoatBounds, _boatBounds);
        _mat.SetColor(ID_Haze, _haze);
        _mat.SetFloat(ID_HazeStart, hazeStart);
        _mat.SetFloat(ID_HazeEnd, hazeEnd);
        _mat.SetFloat(ID_WaveScale, waveScale);
        _mat.SetFloat(ID_Choppiness, choppiness);
        _mat.SetFloat(ID_Ripple, rippleStrength);
        _mat.SetFloat(ID_Patches, windPatches);
        _mat.SetFloat(ID_FoamAmount, foamAmount);
        _mat.SetColor(ID_Deep, deepColor);
        _mat.SetColor(ID_Shallow, crestScatterColor);
        _mat.SetColor(ID_Foam, foamColor);
        _mat.SetColor(ID_Zenith, skyZenithColor);
        _mat.SetFloat(ID_SeaLevel, seaLevel);
        _mat.SetFloat(ID_GridK, GridK);
        _mat.SetFloat(ID_Outer, OuterRadius);
    }

    // 메인 카메라 설정을 그대로 복제한 임시 카메라로 스카이박스만 그려, 수평선 바로 위 색(포스트 이전, 선형)을 측정한다.
    // 화면 세로 중앙 행이 정확히 수평선(0°)이 되도록 피치 0 으로 찍고, 중앙보다 2픽셀 위를 읽는다.
    static Color SampleSkyHorizon(Color fallback)
    {
        if (RenderSettings.skybox == null) return fallback;
        const int S = 64;
        var rt = RenderTexture.GetTemporary(S, S, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var go = new GameObject("SkyProbeCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var main = Camera.main;
        if (main != null) cam.CopyFrom(main);
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.cullingMask = 0;
        cam.fieldOfView = 20f;
        cam.targetTexture = rt;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = false;
        var tex = new Texture2D(S, S, TextureFormat.RGBAHalf, false, true);
        Color sum = Color.black;
        int n = 0;
        var prev = RenderTexture.active;
        try
        {
            for (int k = 0; k < 8; k++)
            {
                go.transform.SetPositionAndRotation(main != null ? main.transform.position : Vector3.zero, Quaternion.Euler(0f, k * 45f, 0f));
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, S, S), 0, 0);
                tex.Apply();
                for (int x = S / 2 - 8; x < S / 2 + 8; x++) { sum += tex.GetPixel(x, S / 2 + 2); n++; }
            }
        }
        catch { return fallback; }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            DestroySafe(tex);
            DestroySafe(go);
        }
        var c = sum / Mathf.Max(1, n);
        if (c.maxColorComponent <= 0.001f) return fallback;
        c.a = 1f;
        return c;
    }

#if UNITY_EDITOR
    // 항모 모델의 실제 흘수선을 측정해 선체 파라미터를 채운다. 선체 메쉬가 CPU 읽기 불가라 임시 MeshCollider 에
    // 해수면 +0.3m 높이로 좌우 레이를 쏘아 단면 폭을 재고, 측정 후 콜라이더는 지운다.
    [ContextMenu("흘수선 자동 측정")]
    void MeasureWaterline()
    {
        if (carrier == null) { Debug.LogWarning("[OceanSurface] carrier 미지정"); return; }
        var temp = new System.Collections.Generic.List<Collider>();
        foreach (var mf in carrier.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            temp.Add(mc);
        }
        Physics.SyncTransforms();
        // 선체 임시 콜라이더에만 쏜다 — 항모에 붙은 다른 콜라이더(존 트리거 등)에 맞지 않도록
        bool HullRay(Vector3 origin, Vector3 dir, float dist, out RaycastHit hit)
        {
            hit = default; bool any = false; float best = dist;
            var ray = new Ray(origin, dir);
            foreach (var c in temp)
                if (c.Raycast(ray, out var h, best)) { hit = h; best = h.distance; any = true; }
            return any;
        }
        try
        {
            float y = seaLevel + 0.3f;
            var rows = new System.Collections.Generic.List<(float z, float left, float right)>();
            for (float z = -200f; z <= 200f; z += 2f)
            {
                Vector3 o = carrier.TransformPoint(new Vector3(0f, 0f, z)); o.y = y;
                bool hr = HullRay(o + carrier.right * 120f, -carrier.right, 240f, out var h1);
                bool hl = HullRay(o - carrier.right * 120f, carrier.right, 240f, out var h2);
                if (hr && hl)
                    rows.Add((z, carrier.InverseTransformPoint(h2.point).x, carrier.InverseTransformPoint(h1.point).x));
            }
            if (rows.Count < 5) { Debug.LogWarning("[OceanSurface] 흘수선 측정 실패 — 해수면 높이에서 선체를 찾지 못함"); return; }

            float maxHalf = 0f, cxSum = 0f; int cxN = 0;
            foreach (var r in rows) maxHalf = Mathf.Max(maxHalf, (r.right - r.left) * 0.5f);
            foreach (var r in rows)
                if ((r.right - r.left) * 0.5f > maxHalf * 0.97f) { cxSum += (r.left + r.right) * 0.5f; cxN++; }
            float zFirst = rows[0].z, zLast = rows[rows.Count - 1].z;
            // 진행 방향(선수)이 로컬 -z 인지 +z 인지에 따라 선수/선미 끝 결정
            bool bowNegZ = forwardLocal.z < 0f;
            float bowZ = bowNegZ ? zFirst - 1f : zLast + 1f;
            float sternZ = bowNegZ ? zLast + 1f : zFirst - 1f;
            // 최대 폭의 95% 에 처음 닿는 지점 → 선수부/선미부 길이
            float fullFromBow = 0f, fullFromStern = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[bowNegZ ? i : rows.Count - 1 - i];
                if ((r.right - r.left) * 0.5f >= maxHalf * 0.95f) { fullFromBow = Mathf.Abs(r.z - bowZ); break; }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[bowNegZ ? rows.Count - 1 - i : i];
                if ((r.right - r.left) * 0.5f >= maxHalf * 0.95f) { fullFromStern = Mathf.Abs(r.z - sternZ); break; }
            }
            // 흘수: 선체 최저점 ~ 해수면
            var rends = carrier.GetComponentsInChildren<Renderer>();
            float minY = float.MaxValue;
            foreach (var rd in rends) minY = Mathf.Min(minY, rd.bounds.min.y);

            UnityEditor.Undo.RecordObject(this, "흘수선 자동 측정");
            hullCenterLocal = new Vector3(cxN > 0 ? cxSum / cxN : 0f, 0f, (bowZ + sternZ) * 0.5f);
            hullHalfLength = Mathf.Abs(sternZ - bowZ) * 0.5f;
            hullHalfBeam = maxHalf;
            // 셰이더 단면식이 95% 폭에 닿는 비율로 환산: 선수 pow(s,0.95)=0.95 → s≈0.947, 선미 0.3+0.7√s=0.95 → s≈0.86
            bowEntryLength = Mathf.Max(5f, fullFromBow / 0.947f);
            sternRunLength = Mathf.Max(5f, fullFromStern / 0.86f);
            if (minY < seaLevel) draft = seaLevel - minY;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[OceanSurface] 흘수선: 중심 {hullCenterLocal}, 반길이 {hullHalfLength:F1}, 반폭 {hullHalfBeam:F2}, 선수부 {bowEntryLength:F0}, 선미부 {sternRunLength:F0}, 흘수 {draft:F1}");
        }
        finally
        {
            foreach (var c in temp) DestroyImmediate(c);
        }
    }
#endif

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    // 극좌표 격자: 중심 근처는 촘촘, 바깥은 지수적으로 성기게 — 화면상 삼각형 크기가 대체로 균일
    static Mesh BuildPolarGrid()
    {
        var v = new Vector3[(Rings + 1) * Segments + 1];
        v[0] = Vector3.zero;
        float k = Mathf.Log(OuterRadius / InnerRadius) / Rings;
        for (int r = 0; r <= Rings; r++)
        {
            float rad = InnerRadius * Mathf.Exp(k * r);
            // 링마다 반 칸씩 엇갈려 삼각형 대각선 방향이 한쪽으로 줄지어 보이지 않게
            float off = (r & 1) * 0.5f;
            for (int s = 0; s < Segments; s++)
            {
                float a = (s + off) / Segments * Mathf.PI * 2f;
                v[1 + r * Segments + s] = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
            }
        }
        var t = new int[Segments * 3 + Rings * Segments * 6];
        int n = 0;
        for (int s = 0; s < Segments; s++)
        {
            t[n++] = 0; t[n++] = 1 + (s + 1) % Segments; t[n++] = 1 + s;
        }
        for (int r = 0; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                int a = 1 + r * Segments + s, b = 1 + r * Segments + (s + 1) % Segments;
                int c = a + Segments, d = b + Segments;
                t[n++] = a; t[n++] = b; t[n++] = c;
                t[n++] = b; t[n++] = d; t[n++] = c;
            }
        var m = new Mesh { name = "OceanPolarGrid", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
        m.vertices = v;
        m.triangles = t;
        m.bounds = new Bounds(Vector3.zero, new Vector3(OuterRadius * 2f, 60f, OuterRadius * 2f));
        m.UploadMeshData(true);
        return m;
    }
}
