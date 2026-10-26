using Newtonsoft.Json;

// PLAYER_KILLED (TCP): 피격자 소유 클라이언트(플레이어 본인 / AI 는 호스트)가 사망을 보고한다.
// 서버는 점수를 집계하고 방 전체(발신자 포함)에 중계한 뒤 스코어보드를 갱신한다.
public class PlayerKilledPacket : Packet
{
    public string victim  = "";
    public string killer  = "";   // 없으면 "" (자멸·추락)
    public string weapon  = "";   // MSL / GUN / ""
    public string cause   = "";   // SHOT_DOWN / WATER_IMPACT / CRASH / EJECTED
    public float  posX, posY, posZ;
    public int    aircraftId = -1;
}

public class ScoreEntry
{
    public string nickname = "";
    public int    kills;
    public int    aiKills;
    public int    deaths;
}

public class ScoreboardPacket : Packet
{
    public List<ScoreEntry> entries = new();
}

// AIRCRAFT_STATE (TCP): 기체 잔해 처리/갑판 복귀를 방 전체에 중계 (state 0=잔해 소멸, 1=갑판 재배치)
public class AircraftStatePacket : Packet
{
    public string nickname = "";
    public int    aircraftId = -1;
    public int    state;
    public float  posX, posY, posZ;
}

class CombatHandler
{
    public static void HandleKilled(ClientSession session, string json)
    {
        var player = session.player;
        if (player?.room is not Room room) return;

        var p = JsonConvert.DeserializeObject<PlayerKilledPacket>(json);
        if (p == null || string.IsNullOrEmpty(p.victim)) return;

        // 플레이어 사망은 본인만, AI(BOT_*) 사망은 호스트만 보고할 수 있다
        bool victimIsBot = p.victim.StartsWith("BOT_");
        if (victimIsBot ? player.nickname != room.hostNickname : p.victim != player.nickname)
            return;

        room.RecordKill(p.victim, p.killer, victimIsBot);
        room.Broadcast(p);
        room.BroadcastScoreboard();
        Console.WriteLine($"[Kill] {p.killer} -> {p.victim} ({p.weapon} {p.cause})");
    }

    public static void HandleAircraftState(ClientSession session, string json)
    {
        var player = session.player;
        if (player?.room == null) return;

        var p = JsonConvert.DeserializeObject<AircraftStatePacket>(json);
        if (p == null) return;

        p.nickname = player.nickname;
        foreach (Player target in player.room.GetPlayers())
        {
            if (target == player || target.session == null) continue;
            ServerSender.SendPacket(target.session, p);
        }
    }
}
