using System.Net;
using Newtonsoft.Json;

class AIMoveHandler
{
    // AI_MOVE UDP: 발신자(호스트) 제외, 방 내 다른 클라이언트에게 브로드캐스트
    public static void HandleUDP(IPEndPoint remoteEP, string json)
    {
        var session = SessionManager.FindByUDP(remoteEP);
        if (session?.player?.room == null) return;

        var packet = JsonConvert.DeserializeObject<AIMovePacket>(json);
        if (packet == null) return;

        session.player.room.UpdateAircraftPose(packet.nickname,
            packet.posX, packet.posY, packet.posZ,
            packet.rotX, packet.rotY, packet.rotZ,
            packet.speed, true);

        foreach (var player in session.player.room.GetPlayers())
        {
            if (player == session.player) continue;
            if (player.session == null) continue;

            if (player.session.udpEndPoint != null)
                UdpSender.Send(player.session.udpEndPoint, packet);
            else
                ServerSender.SendPacket(player.session, packet);
        }
    }
}
