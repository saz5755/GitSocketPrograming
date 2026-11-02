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

class TankerCallHandler
{
    const double CooldownSec = 20.0;

    // 방 안의 누구나 호출 가능 — 호출자 이름은 세션 기준으로 덮어써 위장 차단, 방 단위 쿨다운으로 연타 방지
    public static void Handle(ClientSession session, string json)
    {
        var room = session.player?.room;
        if (room == null) return;
        var p = JsonConvert.DeserializeObject<TankerCallPacket>(json);
        if (p == null) return;
        if (!float.IsFinite(p.posX) || !float.IsFinite(p.posY) || !float.IsFinite(p.posZ)
            || !float.IsFinite(p.fwdX) || !float.IsFinite(p.fwdZ)) return;

        var now = DateTime.UtcNow;
        lock (room)
        {
            if ((now - room.lastTankerCall).TotalSeconds < CooldownSec)
            {
                Console.WriteLine($"[Room] TANKER_CALL throttled from {session.player!.nickname}");
                return;
            }
            room.lastTankerCall = now;
        }
        p.caller = session.player!.nickname;
        Console.WriteLine($"[Room] {room.roomId} TANKER_CALL by {p.caller}");
        room.Broadcast(p);
    }
}
