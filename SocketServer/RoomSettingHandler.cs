using Newtonsoft.Json;

class RoomSettingHandler
{
    // 방장 권한 검증 — 방장이 아니거나 전투 방이 아니면 무시
    public static void Handle(ClientSession session, string json)
    {
        var room = session.player?.room;
        if (room == null) return;
        var p = JsonConvert.DeserializeObject<RoomSettingPacket>(json);
        if (p == null) return;
        if (session.player!.nickname != room.hostNickname || !room.IsCombat)
        {
            Console.WriteLine($"[Room] SETTING rejected (not host) from {session.player.nickname}");
            return;
        }
        room.enemySkill = Math.Clamp(p.enemySkill, 0, 2);
        p.enemySkill = room.enemySkill;
        Console.WriteLine($"[Room] {room.roomId} enemySkill={room.enemySkill} by {session.player.nickname}");
        room.Broadcast(p);
    }
}
