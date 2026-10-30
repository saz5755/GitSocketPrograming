using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// 원격 기체 보간용 시계 (v4.4 에어쇼 동기화).
///
/// 문제: 스냅샷에 "도착 시각"을 찍으면 네트워크 지터가 그대로 위치 떨림이 되고,
///       같은 호스트가 보낸 리더(MOVE)와 편대(AI)가 서로 다른 지터로 흔들려 밀집 편대가 출렁인다.
/// 해결: 송신 측이 패킷에 자기 시계(ms)를 싣고, 수신 측은 송신자별로 시계 차이를 추정해
///       스냅샷을 "송신 시각" 타임라인에 놓는다. 같은 송신자(호스트)의 리더·편대는 같은 추정치를 공유하므로
///       게스트 화면에서도 호스트가 본 편대 간격이 그대로 재현된다.
///
/// 시계 차이 추정: (수신 시각 − 송신 시각) 의 최솟값 = 최단 지연 경로. 최솟값에 고정되면 송신 측
/// 시계 드리프트·경로 변화에 못 따라가므로 초당 10ms 씩 위로 풀어 주고 더 작은 표본이 오면 다시 내린다.
/// 소켓 수신 스레드에서 호출되므로 스레드 안전하다 (Stopwatch 기반, Unity API 미사용).
/// </summary>
public static class NetClock
{
    static readonly Stopwatch s_watch = Stopwatch.StartNew();

    /// <summary>프로세스 시작 후 경과 초 — 어느 스레드에서나 호출 가능</summary>
    public static double Now => s_watch.Elapsed.TotalSeconds;

    /// <summary>벽시계 ms (어느 스레드에서나)</summary>
    public static uint NowMs => (uint)(s_watch.ElapsedMilliseconds & 0xFFFFFFFF);

    /// <summary>
    /// 패킷에 싣는 송신 시각 — 이번 프레임의 시작 시각 (ms). 메인 스레드 전용.
    /// 자세는 프레임 단위로 진행하므로 송신 순간의 벽시계를 찍으면 최대 한 프레임(100 m/s 면 1.5m) 어긋난다.
    /// 같은 프레임에 보내는 리더 MOVE 와 편대 배치가 정확히 같은 시각을 갖는다.
    /// </summary>
    public static uint FrameMs => (uint)(((ulong)(UnityEngine.Time.unscaledTimeAsDouble * 1000.0) + 1) & 0xFFFFFFFF);   // +1: 0 은 '시각 없음(구버전)'

    const double CreepPerSec = 0.010;   // 최솟값 고정 해제 속도
    const double ResetJump   = 2.0;     // 송신 측 재시작 등으로 차이가 이만큼 튀면 재초기화

    class Sender
    {
        public double offset, jitter, lastRecv;
        public bool   init;
    }

    static readonly Dictionary<string, Sender> s_senders = new();
    static readonly object s_lock = new();

    /// <summary>송신 시각(ms)을 로컬 Now 타임라인으로 변환. sendMs==0(구버전·JSON) 이면 수신 시각 그대로.</summary>
    public static double ToLocal(string sender, uint sendMs)
    {
        double recv = Now;
        if (sendMs == 0 || string.IsNullOrEmpty(sender)) return recv;
        double sent = sendMs / 1000.0;
        double sample = recv - sent;
        lock (s_lock)
        {
            if (!s_senders.TryGetValue(sender, out var s)) s_senders[sender] = s = new Sender();
            if (!s.init || System.Math.Abs(sample - s.offset) > ResetJump)
            {
                s.offset = sample; s.jitter = 0.0; s.init = true;
            }
            else
            {
                s.offset += CreepPerSec * System.Math.Max(0.0, recv - s.lastRecv);
                if (sample < s.offset) s.offset = sample;
                // 최단 지연보다 늦게 도착한 정도 = 지터 (지수 평활)
                s.jitter += (sample - s.offset - s.jitter) * 0.05;
            }
            s.lastRecv = recv;
            return sent + s.offset;
        }
    }

    /// <summary>송신자별 보간 지연에 더할 지터 여유 (초)</summary>
    public static float JitterOf(string sender)
    {
        if (string.IsNullOrEmpty(sender)) return 0f;
        lock (s_lock)
            return s_senders.TryGetValue(sender, out var s) ? (float)s.jitter : 0f;
    }

    public static void Forget(string sender)
    {
        lock (s_lock) s_senders.Remove(sender);
    }
}
