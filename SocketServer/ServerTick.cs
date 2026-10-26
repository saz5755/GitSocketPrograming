// 서버 틱: UDP 패킷 유실 대비 주기적 상태 재전송 (20Hz = 50ms)
class ServerTick
{
    const int TICK_MS = 50;

    static Thread tickThread = null!;

    public static void Start()
    {
        tickThread = new Thread(TickLoop) { IsBackground = true };
        tickThread.Start();
    }

    static void TickLoop()
    {
        while (true)
        {
            Tick();
            Thread.Sleep(TICK_MS);
        }
    }

    static int _sweepCounter;

    static void Tick()
    {
        // 1초마다 유령 방 정리
        if (++_sweepCounter >= 1000 / TICK_MS)
        {
            _sweepCounter = 0;
            try { RoomManager.SweepEmptyRooms(); }
            catch (Exception e) { Console.WriteLine($"[Tick] Room sweep error: {e.Message}"); }
        }

        DateTime now = DateTime.UtcNow;
        foreach (ClientSession session in SessionManager.GetSessions())
        {
            Player? player = session.player;
            if (player?.room == null) continue;

            // 아직 MOVE 를 한 번도 안 보낸 플레이어(씬 로딩 중)는 원점 좌표뿐이라 재전송하지 않는다
            if (player.lastProcessedTick == 0) continue;

            // 계속 송신 중인 플레이어는 MoveHandler 가 이미 같은 틱을 브로드캐스트했고 다음 패킷이 50ms 안에 덮어쓴다.
            // 재전송은 송신이 멈춘 플레이어(마지막 패킷 유실 대비)에게만 의미가 있다.
            // (기존: 전원 20Hz 재전송 → 수신 MOVE 의 절반이 중복, 게다가 isFlying=false 로 나가 원격 기체 스냅샷이 버려짐)
            if (now - player.lastInputTime < IdleResendAfter) continue;

            MoveHandler.BroadcastMove(player, player.lastProcessedTick);
        }
    }

    static readonly TimeSpan IdleResendAfter = TimeSpan.FromMilliseconds(200);
}
