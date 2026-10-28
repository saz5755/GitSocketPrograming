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
        Process(session, packet);
    }

    public static void HandleBinary(IPEndPoint remoteEP, byte[] data)
    {
        var session = SessionManager.FindByUDP(remoteEP);
        if (session?.player?.room == null) return;
        session.binaryUdp = true;
        var packet = BinaryProtocol.DecodeAIMove(data);
        if (packet != null) Process(session, packet);
    }

    static void Process(ClientSession session, AIMovePacket packet)
    {
        var room = session.player!.room!;
        // AI 기체는 호스트만 움직일 수 있다
        if (session.player.nickname != room.hostNickname || !packet.nickname.StartsWith("BOT_")) return;
        room.RecordPose(packet.nickname, packet.posX, packet.posY, packet.posZ);

        room.UpdateAircraftPose(packet.nickname,
            packet.posX, packet.posY, packet.posZ,
            packet.rotX, packet.rotY, packet.rotZ,
            packet.speed, true);

        UdpSender.BroadcastToRoom(room, session.player, packet, () => BinaryProtocol.EncodeAIMove(packet));
    }
}
