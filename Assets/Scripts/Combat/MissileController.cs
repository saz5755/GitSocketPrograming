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

    // ── AIM-120 유도 단계 ──────────────────────────────────────────────────
    enum GuidancePhase { Midcourse, Terminal }

    // ARH 시커 활성 거리 — 이 안에 들어오면 종말유도로 전환
    const float ARH_ACTIVATION_RANGE = 8000f;
    // 중간유도 데이터링크 갱신 주기 (s) — 예측 교점 재계산
    const float DATALINK_INTERVAL    = 1.5f;
    // 스냅샷 딜레이 보상 (s) — 원격 위치 데이터가 0.1s 지연되므로 예측 보정
    const float LAG_COMPENSATION     = 0.1f;
    // 속도 추정 지수 평활 계수 (높을수록 빠른 반응, 노이즈 증가)
    const float VEL_SMOOTH           = 0.20f;

    GuidancePhase _phase         = GuidancePhase.Midcourse;
    Vector3       _targetVelEst;            // 타겟 속도 추정 (딜레이 보상·중간유도 공용)
    Vector3       _prevTargetPos;           // 이전 프레임 타겟 위치
    Vector3       _interceptPoint;          // 중간유도 목표 교점
    float         _datalinkTimer;
    bool          _prevTargetPosValid;

    // ── 공개 상태 ──────────────────────────────────────────────────────────
    public Transform           Target       { get; private set; }
    public bool                IsDeflected  { get; private set; }
    public MissileGuidanceType GuidanceType { get; private set; }
    public string              MissileId    { get; private set; }
    public string              ShooterNick  { get; private set; }
    public bool                IsARHActive  { get; private set; }

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

        if (target != null)
        {
            _prevTargetPos      = target.position;
            _interceptPoint     = target.position;
            _prevTargetPosValid = true;
        }

        // HeatSeeking은 처음부터 Terminal (적외선 시커 = 항상 활성)
        if (guidanceType == MissileGuidanceType.HeatSeeking)
        {
            _phase      = GuidancePhase.Terminal;
            IsARHActive = false;
        }

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

        // ── 타겟 속도 추정 (지수 평활) ─────────────────────────────────────
        if (Target != null && _prevTargetPosValid && Time.deltaTime > 0f)
        {
            Vector3 rawVel = (Target.position - _prevTargetPos) / Time.deltaTime;
            _targetVelEst  = Vector3.Lerp(_targetVelEst, rawVel, VEL_SMOOTH);
        }
        if (Target != null)
        {
            _prevTargetPos      = Target.position;
            _prevTargetPosValid = true;
        }

        // ── ARH 전환 판정 (RadarGuided 전용) ───────────────────────────────
        if (GuidanceType == MissileGuidanceType.RadarGuided
            && _phase == GuidancePhase.Midcourse
            && Target != null
            && Vector3.Distance(transform.position, Target.position) <= ARH_ACTIVATION_RANGE)
        {
            _phase      = GuidancePhase.Terminal;
            IsARHActive = true;
            _prevLOS    = Vector3.zero;  // 위상 전환 시 LOS 히스토리 초기화 (첫 프레임 LOSRate 급등 방지)
            BroadcastARHActive();
        }

        // ── 디코이 체크 (0.25초마다) ────────────────────────────────────────
        if (_armed && !IsDeflected)
        {
            _decoyTimer += Time.deltaTime;
            if (_decoyTimer >= 0.25f) { _decoyTimer = 0f; CheckCountermeasures(); }
        }

        Vector3 posBefore = transform.position;

        if (Target != null)
        {
            if (_phase == GuidancePhase.Midcourse)
                NavigateMidcourse();
            else
                NavigateTerminal();
        }
        else
        {
            transform.position += transform.forward * _speed * Time.deltaTime;
        }

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

    // ── 중간유도: 예측 교점을 향한 PN 유도 ───────────────────────────────
    // 타겟 위치를 직접 추적하지 않고, 속도 기반으로 예측한 요격 교점으로 비행.
    // 타겟 RWR에는 "RADAR TRACKING" 수준으로만 표시 (시커 미방사).
    void NavigateMidcourse()
    {
        _datalinkTimer += Time.deltaTime;
        if (_datalinkTimer >= DATALINK_INTERVAL || _interceptPoint == Vector3.zero)
        {
            _datalinkTimer  = 0f;
            _interceptPoint = PredictIntercept();
        }

        NavigateToward(_interceptPoint);
    }

    // ── 종말유도: ARH + 딜레이 보상 ──────────────────────────────────────
    void NavigateTerminal()
    {
        float dist = Vector3.Distance(transform.position, Target.position);

        // 근접 최종 단계 (< 80m): PN 유도 불안정 구간 — 순수 추격으로 전환
        // 이 거리에서 근접 신관(8m)이 터지므로 PN 정밀도보다 빠른 방향 수정이 우선
        if (dist < 80f)
        {
            Vector3 toTarget = (Target.position - transform.position).normalized;
            float   dt       = Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(toTarget), maxTurnRate * 4f * dt);
            transform.position += transform.forward * _speed * dt;
            _prevLOS = toTarget;
            return;
        }

        // 딜레이 보상: 150m 이하에서 0으로 수렴 (속도 추정 노이즈 방지)
        // 원거리에서만 lag compensation이 실질적 이득을 가짐
        float lagScale      = Mathf.Clamp01((dist - 150f) / 600f);
        Vector3 aimPos      = Target.position + _targetVelEst * (LAG_COMPENSATION * lagScale);
        NavigateToward(aimPos);
    }

    void NavigateToward(Vector3 targetPos)
    {
        Vector3 LOS = (targetPos - transform.position).normalized;
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

    // ── 예측 교점 계산 ────────────────────────────────────────────────────
    // 2회 반복 수렴으로 미사일 비행 시간과 타겟 이동을 동시에 고려
    Vector3 PredictIntercept()
    {
        if (Target == null) return _interceptPoint;

        float timeEst = Vector3.Distance(transform.position, Target.position) / Mathf.Max(_speed, 1f);

        for (int i = 0; i < 2; i++)
        {
            Vector3 predicted = Target.position + _targetVelEst * timeEst;
            timeEst = Vector3.Distance(transform.position, predicted) / Mathf.Max(_speed, 1f);
        }

        return Target.position + _targetVelEst * timeEst;
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

    // ── 채프 도플러 기반 교란 효율 계산 ──────────────────────────────────
    // 레이더 유도 미사일은 펄스-도플러 탐색기를 사용.
    // 채프와 타겟의 방사 속도(미사일 방향 성분)가 비슷할수록 구별 불가 → 교란 성공.
    // 타겟이 기동으로 속도를 바꾸면 도플러 차이가 커져 채프 효과 감소.
    float CalcChaffEffectiveness(CountermeasureDecoy decoy, float dist)
    {
        Vector3 missileToDecoy  = (decoy.Position - transform.position).normalized;
        Vector3 missileToTarget = (Target.position - transform.position).normalized;

        // 방사 속도 = 미사일 방향 성분의 도플러 특성
        float chaffDoppler  = Vector3.Dot(decoy.Velocity,  missileToDecoy);
        float targetDoppler = Vector3.Dot(_targetVelEst,   missileToTarget);

        // 도플러 차이가 레이더 필터 대역폭 이하면 구별 불가 → 교란 가능
        float dopplerDiff = Mathf.Abs(chaffDoppler - targetDoppler);
        const float dopplerBandwidth = 20f;  // m/s — 펄스-도플러 필터 대역폭
        float dopplerMatch = Mathf.Clamp01(1f - dopplerDiff / dopplerBandwidth);

        // 각도 분리: 채프가 타겟과 같은 레이더 빔 안에 있어야 함
        float angularSep = Vector3.Angle(missileToDecoy, missileToTarget);
        const float beamWidth = 4f;          // degrees — 레이더 빔 폭
        float angularMatch = Mathf.Clamp01(1f - angularSep / beamWidth);

        // 거리 요인 (보조)
        float distFactor = Mathf.Clamp01(1f - dist / 120f);

        return dopplerMatch * angularMatch * 0.85f + distFactor * 0.10f;
    }

    void CheckCountermeasures()
    {
        foreach (var decoy in CountermeasureSystem.Active)
        {
            bool compatible = GuidanceType == MissileGuidanceType.HeatSeeking
                            ? decoy.Type == CountermeasureType.Flare
                            : decoy.Type == CountermeasureType.Chaff;
            if (!compatible) continue;

            float dist = Vector3.Distance(transform.position, decoy.Position);
            if (dist > 120f) continue;

            float prob;
            if (GuidanceType == MissileGuidanceType.RadarGuided)
                prob = CalcChaffEffectiveness(decoy, dist);
            else
                prob = Mathf.Lerp(0.85f, 0.05f, dist / 120f);  // 플레어: 거리 기반 유지

            if (Random.value < prob)
            {
                Target      = decoy.transform;
                IsDeflected = true;
                return;
            }
        }
    }

    void Detonate()
    {
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

    // ARH 활성화 시 타겟에게 상위 경고 레벨 브로드캐스트
    void BroadcastARHActive()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || string.IsNullOrEmpty(ShooterNick)) return;
        if (sc.myNickname != ShooterNick) return;

        var targetPc = Target?.GetComponent<PlayerController>();
        if (targetPc == null) return;

        sc.SendMissileWarn(ShooterNick, targetPc.nickname,
                           (int)ThreatWarningSystem.ThreatLevel.MissileActive);
    }

    void BroadcastPosition()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || string.IsNullOrEmpty(ShooterNick)) return;
        if (sc.myNickname != ShooterNick) return;

        sc.SendMissileMove(MissileId, transform.position, transform.eulerAngles, _speed);
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
