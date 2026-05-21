// 서버 틱: UDP 패킷 유실 대비 주기적 상태 재전송 (20Hz = 50ms)
class ServerTick
{
    const int TICK_MS = 50;

    static Thread tickThread;

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

    static void Tick()
    {
        foreach (ClientSession session in SessionManager.GetSessions())
        {
            Player player = session.player;
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

        foreach (Player target in player.room.GetPlayers())
        {
            if (target == player) continue;
            if (target.session == null) continue;

            // UDP endpoint 등록 전이면 TCP로 fallback
            if (target.session.udpEndPoint != null)
                UdpSender.Send(target.session.udpEndPoint, packet);
            else
                ServerSender.SendPacket(target.session, packet);
        }
    }
}
