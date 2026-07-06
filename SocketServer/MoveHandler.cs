using System.Net;
using System.Text;
using Newtonsoft.Json;

class MoveHandler
{
    public static void HandleUDP(IPEndPoint remoteEP, string json)
    {
        ClientSession? session = SessionManager.FindByUDP(remoteEP);

        if (session == null)
        {
            Console.WriteLine($"[UDP] Session not found: {remoteEP}");
            return;
        }

        if (session.player?.room == null)
            return;

        MovePacket? packet = JsonConvert.DeserializeObject<MovePacket>(json);
        if (packet == null) return;

        // tick=0은 지상 이동 패킷 — 순서 보장 불필요, 항상 통과 (클라이언트 수신 측과 동일 처리)
        if (packet.tick > 0 && packet.tick <= session.player.lastProcessedTick)
            return;

        if (packet.tick > 0)
            session.player.lastProcessedTick = packet.tick;
        session.player.lastInputTime = DateTime.UtcNow;

        session.player.posX = packet.posX;
        session.player.posY = packet.posY;
        session.player.posZ = packet.posZ;
        session.player.rotX = packet.rotX;
        session.player.rotY = packet.rotY;
        session.player.rotZ = packet.rotZ;
        session.player.isMove    = packet.isMove;
        session.player.isFlying  = packet.isFlying;
        // isBoardedInCockpit는 TCP COCKPIT_STATE 패킷이 단독으로 관리 — UDP MOVE가 덮어쓰면 안 됨
        session.player.animState = packet.animState;

        // 발신자에게 ACK: 처리 완료 틱 + 서버 저장 위치 에코 (Reconciliation용)
        if (session.udpEndPoint != null)
        {
            var ack = new MoveAckPacket
            {
                type = PacketType.MOVE_ACK,
                tick = packet.tick,
                posX = packet.posX, posY = packet.posY, posZ = packet.posZ,
                rotX = packet.rotX, rotY = packet.rotY, rotZ = packet.rotZ
            };
            try
            {
                byte[] ackData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ack));
                Program.udpServer.Send(ackData, ackData.Length, session.udpEndPoint);
            }
            catch { }
        }

        BroadcastMove(session.player, packet.tick);
    }

    public static void BroadcastMove(Player sender, int broadcastTick = 0)
    {
        MoveBroadcastPacket broadcast = new()
        {
            type = PacketType.MOVE,
            tick = broadcastTick,
            nickname = sender.nickname,
            posX = sender.posX,
            posY = sender.posY,
            posZ = sender.posZ,
            rotX = sender.rotX,
            rotY = sender.rotY,
            rotZ = sender.rotZ,
            isMove             = sender.isMove,
            isFlying           = sender.isFlying,
            isBoardedInCockpit = sender.isBoardedInCockpit,
            animState          = sender.animState
        };

        foreach (Player target in sender.room!.GetPlayers())
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
