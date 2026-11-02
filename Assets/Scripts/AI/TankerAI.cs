using UnityEngine;

/// <summary>
/// 급유기 AI (호스트 전용) — 정해진 급유 궤도(tanker track)를 일정 고도·속도로 도는 레이스트랙 비행.
/// 실제 공중급유처럼 급유기는 예측 가능하게 곧게·완만하게 날고, 수급기가 맞춰 들어온다.
///   · 직선 구간 TrackLength, 양 끝 반원 선회 TurnRadius (표준 선회 경사 ≈ 11°)
///   · 위치는 AI_MOVE_BATCH 로 게스트에게 전달 — 호스·드로그는 각 클라이언트의 TankerDrogueRig 가 그린다
///
/// 급유기 호출 (TANKER_CALL):
///   · Inbound — 호출한 기체 앞쪽 LeadAhead 지점으로 요격 침로를 잡아 고속(TransitSpeed)으로 날아간다
///     (호출자 위치·속도는 매 프레임 추적 — 호출자가 빨리 멀어지면 예측 지점을 쫓는다)
///   · Escort — 호출자 2.5 km 안·비슷한 방위가 되면 TankerStationKeeper 로 동행: 호출자 앞에 합류해 같은 속도로 날며
///     바스켓을 프로브에 가져다 댄다 (수급기는 수평 직진만). 급유가 끝나면 비켜 나 그 자리에서 새 궤도를 잡는다
///   · 호출자를 못 만나면(JoinTimeout) 그 자리에서 궤도를 잡는다
/// </summary>
[DefaultExecutionOrder(-50)]   // LateUpdate 에서 급유기를 옮긴 뒤 TankerDrogueRig(기본 순서)가 바스켓을 그리게
public class TankerAI : MonoBehaviour
{
    public const float Speed        = 68f;     // m/s — 군용 추력(최고 80)으로 여유 있게 맞출 수 있는 속도
    public const float TransitSpeed = 200f;    // 호출받고 날아갈 때 (실제 급유기 순항 ≈ M0.7)
    const float TransitTurnRate     = 12f;     // °/s
    public const float Altitude     = 1500f;   // 해수면 위 (구름층 650~1150m 위)
    const float MinCallAlt  = 1000f, MaxCallAlt = 2800f;   // 호출 시 궤도 고도 범위 (호출자 고도에 맞춤)
    const float TrackLength = 9000f;
    const float TurnRadius  = 2600f;
    const float LookAhead   = 350f;
    const float LeadAhead   = 4000f;   // 호출자 앞 랑데부 지점
    public const float EscortRange = 4000f;   // 호출자와 이만큼 가깝고 방위가 비슷하면 동행 시작
    public const float EscortHeading = 60f;   // 방위 차 허용 (°)
    const float EscortMaxTime = 600f;  // 동행 최대 시간
    const float JoinTimeout = 360f;    // 그래도 못 만나면 그 자리에서 궤도 설정
    const float TrackStartS = 600f;    // 새 궤도에서 시작하는 진행 거리 — 바로 긴 직선 구간

    /// <summary>기본 급유 궤도 중심 — 항모(원점) 기준 북동쪽, 착함 패턴과 겹치지 않게</summary>
    public static Vector3 TrackCenter(float seaY) => new Vector3(5000f, seaY + Altitude, 6000f);
    static readonly Vector3 DefaultAxis = new Vector3(1f, 0f, 0.35f).normalized;   // 직선 구간 방향

    AIBotController _bot;
    float _s;          // 궤도 위 진행 거리
    float _seaY;
    bool  _init;
    Vector3 _center, _axis;

    // 호출 상태 (호스트에서만 의미 있음)
    bool    _inbound;
    string  _caller;
    Vector3 _callPos, _callFwd, _callVel;
    bool    _hasCallPos;
    float   _inboundT;
    PlayerManager _pm;

    // 동행 (호출자 근처에서 같이 날며 급유)
    TankerStationKeeper _keeper;
    float _escortT, _lostT, _escortSpeed;
    float _cruise = Speed;   // 궤도 순항 속도 — 동행이 끝난 직후엔 동행 속도에서 천천히 Speed 로

    public bool   Inbound   => _inbound;
    public bool   Escorting => _keeper != null;
    public TankerStationKeeper Keeper => _keeper;
    public string Caller  => _caller;

    static float Perimeter => 2f * TrackLength + 2f * Mathf.PI * TurnRadius;

    /// <summary>기본 궤도 위 진행 거리 s 의 위치 (스폰용)</summary>
    public static Vector3 PointAt(float s, float seaY) => TrackPoint(s, TrackCenter(seaY), DefaultAxis);

    /// <summary>궤도(중심 c, 직선 방향 axis) 위 진행 거리 s 의 위치 (반시계 = 좌선회)</summary>
    static Vector3 TrackPoint(float s, Vector3 c, Vector3 axis)
    {
        s = Mathf.Repeat(s, Perimeter);
        Vector3 side = Vector3.Cross(Vector3.up, axis);   // 축의 왼쪽
        Vector3 a = c - axis * (TrackLength * 0.5f);       // 직선 시작
        float half = Mathf.PI * TurnRadius;
        if (s < TrackLength) return a + axis * s - side * TurnRadius;              // 아래 직선 (+축 방향)
        s -= TrackLength;
        if (s < half)                                                              // 끝 반원 (좌선회)
        {
            float ang = s / TurnRadius;
            Vector3 cc = a + axis * TrackLength;
            return cc + axis * (Mathf.Sin(ang) * TurnRadius) - side * (Mathf.Cos(ang) * TurnRadius);
        }
        s -= half;
        if (s < TrackLength) return a + axis * (TrackLength - s) + side * TurnRadius;   // 위 직선 (−축)
        s -= TrackLength;
        {
            float ang = s / TurnRadius;
            return a - axis * (Mathf.Sin(ang) * TurnRadius) + side * (Mathf.Cos(ang) * TurnRadius);
        }
    }

    Vector3 P(float s) => TrackPoint(s, _center, _axis);

    public static bool InTurn(float s)
    {
        s = Mathf.Repeat(s, Perimeter);
        float half = Mathf.PI * TurnRadius;
        return (s >= TrackLength && s < TrackLength + half) || s >= 2f * TrackLength + half;
    }

    public void Initialize(float seaY, float startS)
    {
        _bot = GetComponent<AIBotController>();
        _seaY = seaY;
        _s = startS;
        _center = TrackCenter(seaY);
        _axis = DefaultAxis;
        _init = true;
        _bot.SetMaxSpeed(Speed + 5f);
        _bot.SetTurnRate(8f);
        _bot.SetInitialSpeed(Speed);   // 궤도 위에서 이미 순항 중인 상태로 시작
    }

    /// <summary>
    /// 호스트 승계 — 기본 궤도 근처면 가장 가까운 지점부터 이어서 날고,
    /// 호출받아 다른 곳에 궤도를 잡았던 급유기라면 지금 위치·방향으로 궤도를 다시 잡는다.
    /// </summary>
    public void InitializeFromCurrent(float seaY)
    {
        float best = 0f, bestD = float.MaxValue;
        for (float s = 0f; s < Perimeter; s += 100f)
        {
            float d = (PointAt(s, seaY) - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = s; }
        }
        Initialize(seaY, best);
        if (bestD > 1500f * 1500f)
            Establish(transform.forward, transform.position.y);
    }

    /// <summary>
    /// 급유기 호출 (호스트에서 TANKER_CALL 수신 시). callerPos/Fwd 는 호출 당시 값 —
    /// 호출자 기체를 찾을 수 있으면 매 프레임 실제 위치를 따라간다.
    /// </summary>
    public void Call(string caller, Vector3 callerPos, Vector3 callerFwd)
    {
        if (!_init) return;
        if (_keeper != null) EndEscort();
        _caller   = caller;
        _callPos  = callerPos;
        callerFwd.y = 0f;
        _callFwd  = callerFwd.sqrMagnitude > 0.01f ? callerFwd.normalized : transform.forward;
        _inbound  = true;
        _inboundT = 0f;
        _callVel  = Vector3.zero;
        _hasCallPos = false;
        _bot.SetMaxSpeed(TransitSpeed + 5f);
        _bot.SetTurnRate(TransitTurnRate);
        DevLog.Log($"[TankerAI] Called by {caller} at {callerPos}");
    }

    /// <summary>지금 위치에서 dir 방향 직선 구간이 시작되는 새 궤도를 잡는다</summary>
    void Establish(Vector3 dir, float altY)
    {
        dir.y = 0f;
        _axis = dir.sqrMagnitude > 0.01f ? dir.normalized : DefaultAxis;
        Vector3 side = Vector3.Cross(Vector3.up, _axis);
        Vector3 p = transform.position; p.y = altY;
        // TrackPoint(TrackStartS) == p 가 되도록 중심을 역산 (아래 직선: a + axis*s − side*R, a = c − axis*L/2)
        _center = p - _axis * (TrackStartS - TrackLength * 0.5f) + side * TurnRadius;
        _s = TrackStartS;
        _inbound = false;
        _bot.SetMaxSpeed(Mathf.Max(Speed, _cruise) + 5f);
        _bot.SetTurnRate(8f);
        DevLog.Log($"[TankerAI] Track established at {p}, axis {_axis}");
    }

    /// <summary>호출자 기체의 현재 위치·수평 진행 방향 (못 찾으면 호출 당시 값을 추정 이동)</summary>
    bool CallerPose(out Vector3 pos, out Vector3 fwd) => CallerPose(out pos, out fwd, out _);

    bool CallerPose(out Vector3 pos, out Vector3 fwd, out PlayerController pc)
    {
        pc = null;
        string me = NetworkManager.Instance?.socketClient?.myNickname;
        if (_caller == me)
        {
            var gm = GameModeManager.Instance;
            if (gm != null && gm.LocalAircraft != null) pc = gm.LocalAircraft.GetComponent<PlayerController>();
        }
        else
        {
            if (_pm == null) _pm = FindAnyObjectByType<PlayerManager>();
            pc = _pm != null ? _pm.GetRemoteAircraft(_caller) : null;
        }
        if (pc == null) { pos = _callPos; fwd = _callFwd; return false; }
        pos = pc.transform.position;
        fwd = pc.transform.forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : _callFwd;
        // 수평 속도 추정 (원격 기체는 보간 위치라 지터가 있으니 완만하게)
        float dt = Time.deltaTime;
        if (_hasCallPos && dt > 1e-4f)
        {
            Vector3 v = (pos - _callPos) / dt; v.y = 0f;
            if (v.sqrMagnitude < 700f * 700f) _callVel = Vector3.Lerp(_callVel, v, 1f - Mathf.Exp(-dt * 1.5f));
        }
        _hasCallPos = true;
        _callPos = pos; _callFwd = fwd;
        return true;
    }

    void Update()
    {
        if (!_init || _bot == null) return;

        if (_keeper != null) return;   // 동행 중 — LateUpdate 에서 직접 배치
        if (_inbound) { UpdateInbound(); return; }

        // 실제 위치에 가장 가까운 궤도 지점 근방을 찾아 진행 거리 갱신 (궤도에서 밀려도 다시 합류)
        float best = _s, bestD = float.MaxValue;
        for (float d = -150f; d <= 450f; d += 25f)
        {
            float dd = (P(_s + d) - transform.position).sqrMagnitude;
            if (dd < bestD) { bestD = dd; best = _s + d; }
        }
        _s = Mathf.Max(_s, best);

        Vector3 target = P(_s + LookAhead);
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude < 1f) return;
        if (_cruise != Speed)
        {
            _cruise = Mathf.MoveTowards(_cruise, Speed, 0.8f * Time.deltaTime);
            _bot.SetMaxSpeed(Mathf.Max(Speed, _cruise) + 5f);
        }
        float bank = InTurn(_s + LookAhead * 0.5f) ? Mathf.Atan(_cruise * _cruise / (9.81f * TurnRadius)) * Mathf.Rad2Deg : 0f;
        // 좌선회 → 왼쪽 날개 아래 (z 롤 +)
        var rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
        _bot.SetTarget(target, rot, _cruise);
    }

    void UpdateInbound()
    {
        _inboundT += Time.deltaTime;
        bool found = CallerPose(out Vector3 cp, out Vector3 cf, out PlayerController cpc);
        float altY = _seaY + Mathf.Clamp(cp.y - _seaY, MinCallAlt, MaxCallAlt);

        Vector3 lead = cp + cf * LeadAhead; lead.y = altY;
        Vector3 here = transform.position;

        // 동행 시작 — 호출자 근처에서 비슷한 방위로 날고 있을 때 (마주 오는 중이면 랑데부 지점을 지나 돌아선 뒤)
        float dist = Vector3.Distance(here, cp);
        float hdg  = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, Mathf.Atan2(cf.x, cf.z) * Mathf.Rad2Deg));
        if (found && (dist < EscortRange && hdg < EscortHeading || dist < 700f))
        {
            BeginEscort(cpc);
            return;
        }
        if (_inboundT > JoinTimeout)
        {
            Establish(cf, altY);   // 못 만나면 그 자리에서 궤도 — 수급기가 찾아온다
            return;
        }

        // 요격 침로 — 랑데부 지점이 t 초 뒤 있을 곳을 TransitSpeed 로 t 초에 닿는 t 를 반복으로 구한다.
        // 호출자가 더 빨리 멀어지면 수렴하지 않으므로 t 를 상한으로 묶어 예측 지점을 쫓는다
        Vector3 aim = lead;
        float t = 0f;
        for (int i = 0; i < 6; i++)
        {
            aim = lead + _callVel * t;
            t = Mathf.Min(new Vector2(aim.x - here.x, aim.z - here.z).magnitude / TransitSpeed, 90f);
        }
        aim = lead + _callVel * t;

        // 이미 호출자 앞쪽에서 비슷한 방향으로 날고 있으면 되돌아가지 않는다 —
        // 호출자 진행선 위로 붙으며 호출자보다 느리게 날아 따라잡히길 기다린다
        float hdgNow = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, Mathf.Atan2(cf.x, cf.z) * Mathf.Rad2Deg));
        float ahead = Vector2.Dot(new Vector2(here.x - lead.x, here.z - lead.z), new Vector2(cf.x, cf.z));
        bool waitAhead = ahead > 0f && hdgNow < 100f;
        if (waitAhead) aim = lead + cf * (ahead + 1500f);

        Vector3 dir = aim - here;
        if (dir.sqrMagnitude < 1f) return;
        // 목표 자세를 '조금 앞의 기수 방향'으로만 준다 — 큰 각도 차이를 AIBotController 의 RotateTowards 에
        // 한 번에 맡기면 경사·방위가 섞인 최단 회전 경로가 기수를 들어 올려 선회 중 수백 m 상승한다
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float curYaw = transform.eulerAngles.y;
        float err = Mathf.DeltaAngle(curYaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
        float yaw = curYaw + Mathf.Clamp(err, -TransitTurnRate * 0.7f * dt, TransitTurnRate * 0.7f * dt);
        float bank = Mathf.Clamp(-err * 0.8f, -25f, 25f);   // 우선회 → 오른쪽 날개 아래 (z −)
        // 고도는 방향과 따로 — 오차에 비례한 상승·강하율(±15 m/s)로 맞춘다 (Unity 피치 + = 기수 아래)
        float climb = Mathf.Clamp((altY - here.y) * 0.1f, -15f, 15f);
        float pitch = -Mathf.Asin(Mathf.Clamp(climb / TransitSpeed, -0.3f, 0.3f)) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(pitch, yaw, bank);
        // 랑데부 지점 근처에선 호출자 속도로 줄인다 — 더 빠르면 지점을 지나쳐 돌기만 한다
        float toLead = new Vector2(lead.x - here.x, lead.z - here.z).magnitude;
        float spd = waitAhead ? Mathf.Clamp(_callVel.magnitude - 50f, 55f, TransitSpeed)
                  : toLead < 5000f ? Mathf.Clamp(_callVel.magnitude + toLead * 0.03f, 60f, TransitSpeed) : TransitSpeed;
        _bot.SetTarget(aim, rot, spd);
    }

    // ════════════════════════════════════════════════════════════════════
    // 동행 — 호출자 앞에 붙어 같은 속도로 날며 바스켓을 프로브에 맞춘다
    // ════════════════════════════════════════════════════════════════════
    static Vector3 ProbeTip(PlayerController pc)
    {
        var probe = pc.GetComponent<RefuelProbe>();
        return probe != null ? probe.DeployedTipWorld : pc.transform.TransformPoint(new Vector3(0.36f, 1.3f, 5.1f));
    }

    bool CallerIsLocal => _caller == NetworkManager.Instance?.socketClient?.myNickname;

    void BeginEscort(PlayerController pc)
    {
        Vector3 callerVel = CallerIsLocal ? pc.transform.forward * pc.CurrentSpeed : _callVel;
        _keeper = new TankerStationKeeper();
        _keeper.Begin(transform, transform.forward * _bot.Speed, pc.transform, callerVel, ProbeTip(pc));
        _inbound = false;
        _escortT = 0f; _lostT = 0f;
        _escortSpeed = _bot.Speed;
        _bot.PositionOverride = true;
        _bot.SetMaxSpeed(400f);
        _bot.SetTurnRate(720f);   // 자세는 여기서 직접 정한다 — 봇의 회전 제한이 끼어들지 않게
        DevLog.Log($"[TankerAI] Escort start with {_caller}");
    }

    void EndEscort()
    {
        _keeper = null;
        _bot.PositionOverride = false;
        _cruise = Mathf.Clamp(_escortSpeed, 50f, 150f);
        _bot.SetInitialSpeed(_cruise);
        Vector3 f = transform.forward; f.y = 0f;
        float altY = _seaY + Mathf.Clamp(transform.position.y - _seaY, MinCallAlt, MaxCallAlt);
        Establish(f.sqrMagnitude > 0.01f ? f : transform.forward, altY);   // 최대·회전율도 순항값으로 복원
        DevLog.Log($"[TankerAI] Escort end ({_caller})");
    }

    void LateUpdate()
    {
        if (_keeper == null || _bot == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        _escortT += dt;

        bool found = CallerPose(out Vector3 cp, out _, out PlayerController pc);
        // 호출자를 잃거나(퇴장·격추) 저고도(착함 접근)로 내려가면 놓아준다
        if (!found || pc == null || cp.y - _seaY < 250f) { _lostT += dt; if (_lostT > 4f) { EndEscort(); return; } }
        else _lostT = 0f;
        if (pc == null) return;

        bool contact = CallerIsLocal
            ? AerialRefuelSystem.Instance != null && AerialRefuelSystem.Instance.InContact
            : pc.FxRefuelContact;
        bool full = !CallerIsLocal || TankerStationKeeper.TankDone(pc);
        _keeper.Step(dt, pc.transform, ProbeTip(pc), contact, full, out Vector3 pos, out Quaternion rot);

        float spd = (pos - transform.position).magnitude / dt;
        if (spd < 600f) _escortSpeed = Mathf.Lerp(_escortSpeed, spd, 1f - Mathf.Exp(-dt * 4f));
        transform.SetPositionAndRotation(pos, rot);
        _bot.SetTarget(pos, rot, _escortSpeed);
        _bot.SetInitialSpeed(_escortSpeed);   // 원격 외삽용 속도

        if (_keeper.State == TankerStationKeeper.Phase.Done || _escortT > EscortMaxTime) EndEscort();
    }
}
