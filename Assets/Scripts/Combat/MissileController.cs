using UnityEngine;
using UnityEngine.Rendering;

public enum MissileGuidanceType { HeatSeeking, RadarGuided }

public class MissileController : MonoBehaviour
{
    [SerializeField] float topSpeed      = 600f;
    [SerializeField] float acceleration  = 150f;
    [SerializeField] float navConst      = 4f;
    [SerializeField] float maxTurnRate   = 30f;
    [SerializeField] float directHitDist = 3f;
    [SerializeField] float proximityFuse = 8f;
    [SerializeField] float lifetime      = 30f;
    [SerializeField] float armDelay      = 0.5f;

    // ── 공개 상태 ──────────────────────────────────────────────────────────
    public Transform          Target       { get; private set; }
    public bool               IsDeflected  { get; private set; }
    public MissileGuidanceType GuidanceType { get; private set; }
    public string             MissileId    { get; private set; }
    public string             ShooterNick  { get; private set; }

    float   _speed;
    float   _age;
    Vector3 _prevLOS;
    bool    _armed;
    float   _decoyTimer;
    float   _netBroadcastTimer;

    public void Initialize(Transform target, float launchSpeed,
                           MissileGuidanceType guidanceType = MissileGuidanceType.RadarGuided,
                           string missileId = "", string shooterNickname = "")
    {
        Target       = target;
        _speed       = Mathf.Max(launchSpeed, 40f);
        GuidanceType = guidanceType;
        MissileId    = string.IsNullOrEmpty(missileId) ? System.Guid.NewGuid().ToString("N")[..8] : missileId;
        ShooterNick  = shooterNickname;

        AddSmokeTrail();
    }

    void AddSmokeTrail()
    {
        var tr = gameObject.AddComponent<TrailRenderer>();
        tr.time              = 1.8f;
        tr.startWidth        = 0.5f;
        tr.endWidth          = 0.05f;
        tr.minVertexDistance = 0.3f;
        tr.textureMode       = LineTextureMode.Stretch;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows    = false;

        var sh = Shader.Find("Custom/SmokeTrail");
        Material mat;
        if (sh != null)
        {
            mat = new Material(sh);
            mat.SetColor("_Color",      new Color(0.75f, 0.72f, 0.70f, 0.55f));
            mat.SetFloat("_NoiseScale", 3.5f);
        }
        else
        {
            var fallback = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                        ?? Shader.Find("Sprites/Default");
            mat = new Material(fallback);
            mat.color = new Color(0.7f, 0.7f, 0.7f, 0.45f);
        }
        tr.material = mat;

        var gc = new Gradient();
        gc.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.90f, 0.70f), 0.00f),
                new GradientColorKey(new Color(0.65f, 0.62f, 0.60f), 0.30f),
                new GradientColorKey(new Color(0.30f, 0.28f, 0.27f), 1.00f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.80f, 0.00f),
                new GradientAlphaKey(0.45f, 0.40f),
                new GradientAlphaKey(0.00f, 1.00f),
            });
        tr.colorGradient = gc;
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age > lifetime) { Destroy(gameObject); return; }
        if (_age > armDelay) _armed = true;

        _speed = Mathf.MoveTowards(_speed, topSpeed, acceleration * Time.deltaTime);

        // 디코이 체크 (0.25초마다)
        if (_armed && !IsDeflected)
        {
            _decoyTimer += Time.deltaTime;
            if (_decoyTimer >= 0.25f) { _decoyTimer = 0f; CheckCountermeasures(); }
        }

        Vector3 posBefore = transform.position;

        if (Target != null)
            Navigate();
        else
            transform.position += transform.forward * _speed * Time.deltaTime;

        if (_armed && Target != null)
            CheckHit(posBefore);

        // 네트워크 위치 브로드캐스트 (0.1초마다)
        _netBroadcastTimer += Time.deltaTime;
        if (_netBroadcastTimer >= 0.1f)
        {
            _netBroadcastTimer = 0f;
            BroadcastPosition();
        }
    }

    void CheckHit(Vector3 posBefore)
    {
        Vector3 posAfter = transform.position;
        Vector3 move     = posAfter - posBefore;
        float   moveLen  = move.magnitude;

        // 직격 판정: 이동 선분~타겟 최단거리 (서브프레임 정밀도)
        if (moveLen > 0.001f)
        {
            Vector3 toTarget = Target.position - posBefore;
            float   t        = Mathf.Clamp01(Vector3.Dot(toTarget, move) / (moveLen * moveLen));
            float   closest  = Vector3.Distance(posBefore + move * t, Target.position);
            if (closest <= directHitDist) { Detonate(); return; }
        }

        // 근접 신관: 파편 살상반경
        if (Vector3.Distance(posAfter, Target.position) <= proximityFuse)
            Detonate();
    }

    void Navigate()
    {
        Vector3 LOS = (Target.position - transform.position).normalized;
        float   dt  = Time.deltaTime;

        if (dt > 0f && _prevLOS.sqrMagnitude > 0.001f)
        {
            Vector3 LOSRate    = (LOS - _prevLOS) / dt;
            float   closingSpd = Vector3.Dot(transform.forward * _speed, LOS);
            Vector3 desired    = (transform.forward + navConst * closingSpd * LOSRate * dt).normalized;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(desired), maxTurnRate * dt);
        }

        _prevLOS           = LOS;
        transform.position += transform.forward * _speed * dt;
    }

    void CheckCountermeasures()
    {
        foreach (var decoy in CountermeasureSystem.Active)
        {
            // 유도 방식과 디코이 타입 매칭
            bool compatible = GuidanceType == MissileGuidanceType.HeatSeeking
                            ? decoy.Type == CountermeasureType.Flare
                            : decoy.Type == CountermeasureType.Chaff;
            if (!compatible) continue;

            float dist = Vector3.Distance(transform.position, decoy.Position);
            if (dist > 120f) continue;

            // 거리 반비례 확률 (근접할수록 교란 확률 상승)
            float prob = Mathf.Lerp(0.85f, 0.05f, dist / 120f);
            if (Random.value < prob)
            {
                Target      = decoy.transform;
                IsDeflected = true;
                Debug.Log($"[Missile] Deflected by {decoy.Type}");
                return;
            }
        }
    }

    void Detonate()
    {
        // 피격 대상이 로컬 플레이어인지 확인
        bool hitLocal = false;
        if (Target != null)
        {
            var pc = Target.GetComponent<PlayerController>();
            hitLocal = pc != null && pc.isLocalPlayer;
        }

        HitEffectSystem.Instance?.TriggerHit(transform.position, hitLocal);
        BroadcastDestroy(hitLocal);

        Destroy(gameObject);
    }

    void BroadcastPosition()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || string.IsNullOrEmpty(ShooterNick)) return;
        if (sc.myNickname != ShooterNick) return;  // 발사자만 브로드캐스트

        // 주: 서버에서 MISSILE_UPDATE 타입을 추가로 구현해야 완전 동기화됨
        // 현재는 생성/파괴만 TCP 동기화
    }

    void BroadcastDestroy(bool hitLocal)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || string.IsNullOrEmpty(ShooterNick)) return;
        if (sc.myNickname != ShooterNick) return;

        string hitNick = "";
        if (hitLocal && Target != null)
        {
            var pc = Target.GetComponent<PlayerController>();
            if (pc != null) hitNick = pc.nickname;
        }

        sc.SendMissileDestroy(MissileId, ShooterNick, hitNick, transform.position);
    }
}
