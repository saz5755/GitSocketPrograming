using Newtonsoft.Json;

class MissileHandler
{
    public static void HandleSpawn(ClientSession session, string json)
    {
        if (session.player?.room == null) return;
        var p = JsonConvert.DeserializeObject<MissileSpawnPacket>(json);
        if (p == null) return;
        Console.WriteLine($"[Missile] SPAWN  id={p.missileId} shooter={p.shooterNickname}");
        session.player.room.Broadcast(p);
    }

    public static void HandleDestroy(ClientSession session, string json)
    {
        if (session.player?.room == null) return;
        var p = JsonConvert.DeserializeObject<MissileDestroyPacket>(json);
        if (p == null) return;
        Console.WriteLine($"[Missile] DESTROY id={p.missileId} hit={p.hitNickname}");
        session.player.room.Broadcast(p);
    }

    public static void HandleWarn(ClientSession session, string json)
    {
        if (session.player?.room == null) return;
        var p = JsonConvert.DeserializeObject<MissileWarnPacket>(json);
        if (p == null) return;
        session.player.room.Broadcast(p);
    }
}
