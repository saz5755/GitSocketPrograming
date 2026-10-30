using UnityEngine;

/// <summary>
/// 급유기 AI (호스트 전용) — 정해진 급유 궤도(tanker track)를 일정 고도·속도로 도는 레이스트랙 비행.
/// 실제 공중급유처럼 급유기는 예측 가능하게 곧게·완만하게 날고, 수급기가 맞춰 들어온다.
///   · 직선 구간 TrackLength, 양 끝 반원 선회 TurnRadius (표준 선회 경사 ≈ 11°)
///   · 위치는 AI_MOVE_BATCH 로 게스트에게 전달 — 호스·드로그는 각 클라이언트의 TankerDrogueRig 가 그린다
/// </summary>
public class TankerAI : MonoBehaviour
{
    public const float Speed       = 68f;     // m/s — 군용 추력(최고 80)으로 여유 있게 맞출 수 있는 속도
    public const float Altitude    = 1500f;   // 해수면 위 (구름층 650~1150m 위)
    const float TrackLength = 9000f;
    const float TurnRadius  = 2600f;
    const float LookAhead   = 350f;

    /// <summary>급유 궤도 중심 — 항모(원점) 기준 북동쪽, 착함 패턴과 겹치지 않게</summary>
    public static Vector3 TrackCenter(float seaY) => new Vector3(5000f, seaY + Altitude, 6000f);
    static readonly Vector3 Axis = new Vector3(1f, 0f, 0.35f).normalized;   // 직선 구간 방향

    AIBotController _bot;
    float _s;          // 궤도 위 진행 거리
    float _seaY;
    bool  _init;

    static float Perimeter => 2f * TrackLength + 2f * Mathf.PI * TurnRadius;

    /// <summary>궤도 위 진행 거리 s 의 위치 (반시계 = 좌선회)</summary>
    public static Vector3 PointAt(float s, float seaY)
    {
        s = Mathf.Repeat(s, Perimeter);
        Vector3 c = TrackCenter(seaY);
        Vector3 side = Vector3.Cross(Vector3.up, Axis);   // 축의 왼쪽
        Vector3 a = c - Axis * (TrackLength * 0.5f);       // 직선 시작
        float half = Mathf.PI * TurnRadius;
        if (s < TrackLength) return a + Axis * s - side * TurnRadius;              // 아래 직선 (+축 방향)
        s -= TrackLength;
        if (s < half)                                                              // 끝 반원 (좌선회)
        {
            float ang = s / TurnRadius;
            Vector3 cc = a + Axis * TrackLength;
            return cc + Axis * (Mathf.Sin(ang) * TurnRadius) - side * (Mathf.Cos(ang) * TurnRadius);
        }
        s -= half;
        if (s < TrackLength) return a + Axis * (TrackLength - s) + side * TurnRadius;   // 위 직선 (−축)
        s -= TrackLength;
        {
            float ang = s / TurnRadius;
            return a - Axis * (Mathf.Sin(ang) * TurnRadius) + side * (Mathf.Cos(ang) * TurnRadius);
        }
    }

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
        _init = true;
        _bot.SetMaxSpeed(Speed + 5f);
        _bot.SetTurnRate(8f);
        _bot.SetInitialSpeed(Speed);   // 궤도 위에서 이미 순항 중인 상태로 시작
    }

    /// <summary>호스트 승계 — 현재 위치에서 가장 가까운 궤도 지점부터 이어서 난다</summary>
    public void InitializeFromCurrent(float seaY)
    {
        float best = 0f, bestD = float.MaxValue;
        for (float s = 0f; s < Perimeter; s += 100f)
        {
            float d = (PointAt(s, seaY) - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = s; }
        }
        Initialize(seaY, best);
    }

    void Update()
    {
        if (!_init || _bot == null) return;
        float dt = Time.deltaTime;

        // 실제 위치에 가장 가까운 궤도 지점 근방을 찾아 진행 거리 갱신 (궤도에서 밀려도 다시 합류)
        float best = _s, bestD = float.MaxValue;
        for (float d = -150f; d <= 450f; d += 25f)
        {
            float dd = (PointAt(_s + d, _seaY) - transform.position).sqrMagnitude;
            if (dd < bestD) { bestD = dd; best = _s + d; }
        }
        _s = Mathf.Max(_s, best);

        Vector3 target = PointAt(_s + LookAhead, _seaY);
        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude < 1f) return;
        float bank = InTurn(_s + LookAhead * 0.5f) ? Mathf.Atan(Speed * Speed / (9.81f * TurnRadius)) * Mathf.Rad2Deg : 0f;
        // 좌선회 → 왼쪽 날개 아래 (z 롤 +)
        var rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
        _bot.SetTarget(target, rot, Speed);
    }
}
