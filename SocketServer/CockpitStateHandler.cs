using Newtonsoft.Json;

class CockpitStateHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (session.player?.room == null) return;

        var packet = JsonConvert.DeserializeObject<CockpitStatePacket>(json);
        if (packet == null) return;

        Console.WriteLine($"[Cockpit] {session.player.nickname} isBoardedInCockpit={packet.isBoardedInCockpit} pos=({packet.posX:F1},{packet.posY:F1},{packet.posZ:F1})");

        session.player.isBoardedInCockpit = packet.isBoardedInCockpit;

        // 탑승 시 항공기 위치로 서버 좌표 갱신 (이후 스폰 패킷에 반영됨)
        if (packet.isBoardedInCockpit)
        {
            session.player.posX = packet.posX;
            session.player.posY = packet.posY;
            session.player.posZ = packet.posZ;
            session.player.rotX = packet.rotX;
            session.player.rotY = packet.rotY;
            session.player.rotZ = packet.rotZ;
        }

        // 상태 변경은 신뢰성 있는 TCP로 룸 전체 브로드캐스트 (UDP 손실 방지)
        var broadcast = new MoveBroadcastPacket
        {
            type               = PacketType.MOVE,
            nickname           = session.player.nickname,
            posX               = session.player.posX,
            posY               = session.player.posY,
            posZ               = session.player.posZ,
            rotX               = session.player.rotX,
            rotY               = session.player.rotY,
            rotZ               = session.player.rotZ,
            isMove             = false,
            isFlying           = session.player.isFlying,
            isBoardedInCockpit = session.player.isBoardedInCockpit,
            animState          = session.player.animState
        };
        foreach (Player target in session.player.room.GetPlayers())
        {
            if (target == session.player) continue;
            if (target.session == null) continue;
            ServerSender.SendPacket(target.session, broadcast);
        }
    }
}
