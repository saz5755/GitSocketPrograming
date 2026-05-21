using System.Net;
using Newtonsoft.Json;

class MoveHandler
{
    public static void HandleUDP(IPEndPoint remoteEP, string json)
    {
        ClientSession session = SessionManager.FindByUDP(remoteEP);

        if (session == null)
        {
            Console.WriteLine($"[UDP] Session not found: {remoteEP}");
            return;
        }

        if (session.player?.room == null)
            return;

        MovePacket packet = JsonConvert.DeserializeObject<MovePacket>(json);

        if (packet.tick <= session.player.lastProcessedTick)
            return;

        session.player.lastProcessedTick = packet.tick;
        session.player.lastInputTime = DateTime.UtcNow;

        session.player.posX = packet.posX;
        session.player.posY = packet.posY;
        session.player.posZ = packet.posZ;
        session.player.rotX = packet.rotX;
        session.player.rotY = packet.rotY;
        session.player.rotZ = packet.rotZ;
        session.player.isMove = packet.isMove;

        BroadcastMove(session.player);
    }

    public static void BroadcastMove(Player sender)
    {
        MoveBroadcastPacket broadcast = new()
        {
            type = PacketType.MOVE,
            tick = sender.lastProcessedTick,
            nickname = sender.nickname,
            posX = sender.posX,
            posY = sender.posY,
            posZ = sender.posZ,
            rotX = sender.rotX,
            rotY = sender.rotY,
            rotZ = sender.rotZ,
            isMove = sender.isMove
        };

        foreach (Player target in sender.room.GetPlayers())
        {
            if (target == sender) continue;
            if (target.session == null) continue;

            // UDP endpoint 등록 전이면 TCP로 fallback (동기화 보장)
            if (target.session.udpEndPoint != null)
                UdpSender.Send(target.session.udpEndPoint, broadcast);
            else
                ServerSender.SendPacket(target.session, broadcast);
        }
    }
}
