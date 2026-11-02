using UnityEngine;

/// <summary>
/// 급유기 호출 동행 — 급유기가 수급기(호출자)에 맞춰 날며 바스켓을 프로브에 가져다 댄다.
/// 수급기는 수평 직진만 하면 되고, 급유기가 자리를 잡는다 (게임 친화 보조).
///
/// 상태는 '바스켓(정위치) − 프로브 끝' 상대 위치를 호출자 기수 방위 좌표계(x 우, y 위, z 앞)로 들고,
/// 가속 제한이 있는 상대 속도로 목표 위치까지 옮긴다 → 급유기 위치·자세는 매 프레임 여기서 역산.
///   Join       — 호출자 앞 오른쪽 위 편대 위치로 합류 (상대 최대 80 m/s)
///   Precontact — 바스켓을 프로브 22 m 앞에 정렬
///   Approach   — 1.4 m/s 로 바스켓을 프로브에 밀어 넣음 (AerialRefuelSystem 이 접속 판정)
///   Contact    — 1.2 m 밀어 넣은 채 유지, 급유
///   Backoff    — 만탱크로 분리되거나, 접속 없이 오래 지나거나(급유 후 45초·처음 240초), 수급기가 그만 부르면(Dismiss)
///                앞 위로 비켜 남. 만탱크가 아닌데 끊기면 Precontact 로 돌아가 다시 맞춘다
///   Done       — 동행 종료 (호출자 측이 급유기를 놓아준다)
/// 호스트 TankerAI(실제 AI 위치)와 게스트 호출자의 AerialRefuelSystem(로컬 표시 보정)이 같은 로직을 쓴다.
/// </summary>
public class TankerStationKeeper
{
    public enum Phase { Join, Precontact, Approach, Contact, Backoff, Done }
    public Phase State { get; private set; }
    /// <summary>호출자가 급기동 중이라 접근을 멈춘 상태</summary>
    public bool Holding { get; private set; }
    public Vector3 Offset => _o;

    const float ApproachRate  = 1.4f;    // m/s — AerialRefuelSystem.MaxContactClosure(3) 보다 충분히 느리게
    const float ContactPush   = 1.2f;    // 접속 중 밀어 넣은 거리 (허용 5 m)
    const float PrecontactGap = 22f;
    static readonly Vector3 JoinSlot    = new Vector3(28f, 8f, 45f);
    static readonly Vector3 BackoffSlot = new Vector3(30f, 25f, 90f);

    Vector3 _o, _v;          // 상대 위치·속도 (호출자 방위 좌표계)
    float   _frameYaw;       // 호출자 방위 (선회율 제한)
    float   _tankerYaw, _bank, _pitch;
    float   _side = 1f;
    float   _contactT, _missT, _noContactT;
    bool    _hadContact;

    /// <summary>수급기가 동행을 그만 원할 때 — 비켜 나며 끝낸다</summary>
    public void Dismiss() { if (State < Phase.Backoff) State = Phase.Backoff; }

    /// <summary>바스켓을 물어도 되는 단계 (합류·이탈 중 스쳐 지나가며 물리지 않게)</summary>
    public bool AllowsCapture => State == Phase.Approach || State == Phase.Contact;

    /// <summary>동행을 끝낼 만큼 찼는지 — 만탱크 후 분리까지 기다리는 동안 엔진이 태운 양은 무시 (99%)</summary>
    public static bool TankDone(PlayerController pc) => pc != null && pc.CurrentFuel >= pc.MaxFuel * 0.99f;

    /// <summary>현재 급유기 자세에서 동행 시작 — 상대 위치·속도를 이어받아 튀지 않게</summary>
    public void Begin(Transform tanker, Vector3 tankerVel, Transform caller, Vector3 callerVel, Vector3 tipWorld)
    {
        _frameYaw  = caller.eulerAngles.y;
        _tankerYaw = tanker.eulerAngles.y;
        _pitch     = 0f;
        _bank      = 0f;
        var inv = Quaternion.Inverse(Quaternion.Euler(0f, _frameYaw, 0f));
        _o = inv * (tanker.TransformPoint(TankerDrogueRig.BasketRestLocal) - tipWorld);
        _v = inv * (tankerVel - callerVel);
        _side = _o.x >= 0f ? 1f : -1f;
        _contactT = 0f; _missT = 0f; _noContactT = 0f; _hadContact = false;
        State = Phase.Join;
    }

    /// <summary>한 프레임 진행 — 급유기가 있어야 할 위치·자세를 돌려준다</summary>
    /// <param name="full">수급기 연료가 찼는지 — 원격 수급기는 알 수 없으므로 true 로 준다 (끊기면 끝난 것으로)</param>
    public void Step(float dt, Transform caller, Vector3 tipWorld, bool contact, bool full, out Vector3 tankerPos, out Quaternion tankerRot)
    {
        dt = Mathf.Max(dt, 1e-4f);

        if (contact) { _noContactT = 0f; _hadContact = true; }
        else _noContactT += dt;
        if (State < Phase.Contact && _noContactT > (_hadContact ? 45f : 240f)) State = Phase.Backoff;

        // 호출자 방위 좌표계 — 선회율 제한(25°/s)으로 따라가 급선회에도 급유기가 휘둘리지 않게
        float dy = Mathf.DeltaAngle(_frameYaw, caller.eulerAngles.y);
        float yawStep = Mathf.Clamp(dy, -25f * dt, 25f * dt);
        _frameYaw += yawStep;
        float yawRate = yawStep / dt;

        float roll  = Mathf.Abs(Mathf.DeltaAngle(0f, caller.eulerAngles.z));
        float pitch = Mathf.DeltaAngle(0f, caller.eulerAngles.x);   // + = 기수 아래
        Holding = roll > 30f || Mathf.Abs(pitch) > 15f || Mathf.Abs(dy) > 12f;

        Vector3 target; float maxV, accel;
        switch (State)
        {
            case Phase.Join:
                target = new Vector3(JoinSlot.x * _side, JoinSlot.y, JoinSlot.z);
                // 수급기 바로 뒤·옆을 가로지르지 않게 — 가까우면 옆으로 먼저 빠진다
                if (Mathf.Abs(_o.x) < 20f && _o.z < 40f) target.z = _o.z;
                maxV = 80f; accel = 12f;
                if ((_o - target).magnitude < 6f && _v.magnitude < 6f) State = Phase.Precontact;
                break;
            case Phase.Precontact:
                target = Holding ? new Vector3(0f, 2f, PrecontactGap + 15f) : new Vector3(0f, 0f, PrecontactGap);
                maxV = 8f; accel = 3f;
                if (!Holding && (_o - target).magnitude < 0.6f && _v.magnitude < 0.8f) State = Phase.Approach;
                break;
            case Phase.Approach:
                target = new Vector3(0f, 0f, -0.8f);
                maxV = 3f; accel = 2f;
                if (contact) { State = Phase.Contact; _contactT = 0f; break; }
                if (Holding) { State = Phase.Precontact; break; }
                // 바스켓이 흔들려 빗나가면 뒤로 물러서 다시
                _missT = _o.z < -0.5f ? _missT + dt : 0f;
                if (_missT > 1.5f) { _missT = 0f; State = Phase.Precontact; }
                break;
            case Phase.Contact:
                target = new Vector3(0f, 0f, -ContactPush);
                maxV = 1.5f; accel = 2f;
                if (contact) _contactT += dt;
                else State = _contactT > 2f && full ? Phase.Backoff : Phase.Precontact;
                break;
            case Phase.Backoff:
                target = new Vector3(BackoffSlot.x * _side, BackoffSlot.y, BackoffSlot.z);
                maxV = 15f; accel = 4f;
                if ((_o - target).magnitude < 8f) State = Phase.Done;
                break;
            default:
                target = _o; maxV = 0f; accel = 5f;
                break;
        }

        // 원하는 상대 속도 — 거리 비례, 축별 상한. Approach 의 앞뒤는 일정 속도로 천천히
        Vector3 err = target - _o;
        Vector3 want = err * 0.35f;
        if (State == Phase.Approach) want.z = err.z < 0f ? -ApproachRate : Mathf.Min(err.z * 0.5f, 2f);
        if (want.magnitude > maxV) want = want.normalized * maxV;
        _v = Vector3.MoveTowards(_v, want, accel * dt);
        _o += _v * dt;

        // 급유기 자세 — 방위는 기존 기수에서 호출자 방위로 천천히(15°/s), 피치는 호출자를 ±8° 안에서, 경사는 선회율로
        _tankerYaw += Mathf.Clamp(Mathf.DeltaAngle(_tankerYaw, _frameYaw), -15f * dt, 15f * dt);
        _pitch = Mathf.MoveTowards(_pitch, Mathf.Clamp(pitch, -8f, 8f), 10f * dt);
        _bank  = Mathf.MoveTowards(_bank, Mathf.Clamp(-yawRate * 1.5f, -25f, 25f), 20f * dt);
        tankerRot = Quaternion.Euler(_pitch, _tankerYaw, _bank);

        Vector3 basket = tipWorld + Quaternion.Euler(0f, _frameYaw, 0f) * _o;
        tankerPos = basket - tankerRot * TankerDrogueRig.BasketRestLocal;
    }
}
