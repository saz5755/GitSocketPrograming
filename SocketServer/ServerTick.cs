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

        foreach (ClientSession session in SessionManager.GetSessions())
        {
            Player? player = session.player;
            if (player?.room == null) continue;

            BroadcastPlayerState(player);
        }
    }

    static void BroadcastPlayerState(Player player)
    {
        MoveBroadcastPacket packet = new()
        {
            type = PacketType.MOVE,
            tick = player.lastProcessedTick,
            nickname = player.nickname,
            posX = player.posX,
            posY = player.posY,
            posZ = player.posZ,
            rotX = player.rotX,
            rotY = player.rotY,
            rotZ = player.rotZ,
            isMove = player.isMove
        };

        // UDP endpoint 등록 전이면 TCP로 fallback
        UdpSender.BroadcastToRoom(player.room!, player, packet);
    }
}
