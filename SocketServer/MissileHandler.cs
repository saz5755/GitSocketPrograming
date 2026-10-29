using Newtonsoft.Json;

class MissileHandler
{
    public static void HandleSpawn(ClientSession session, string json)
    {
        if (session.player?.room == null) return;
        var p = JsonConvert.DeserializeObject<MissileSpawnPacket>(json);
        if (p == null || string.IsNullOrEmpty(p.missileId)) return;
        if (!HitValidation.CanActAs(session.player.room, session.player, p.shooterNickname)) return;
        session.player.room.RegisterMissile(p.missileId, p.shooterNickname);
        Console.WriteLine($"[Missile] SPAWN  id={p.missileId} shooter={p.shooterNickname}");
        session.player.room.Broadcast(p);
    }

    public static void HandleDestroy(ClientSession session, string json)
    {
        if (session.player?.room == null) return;
        var p = JsonConvert.DeserializeObject<MissileDestroyPacket>(json);
        if (p == null) return;
        // 서버 권위 검증 (v4.3 N-4) — 통과 못 하면 빗나감으로 바꿔 중계 (미사일 소멸 연출은 모두에게 보여야 함)
        if (!HitValidation.ValidateMissileHit(session.player.room, session.player, p, out var reason))
        {
            if (!HitValidation.CanActAs(session.player.room, session.player, p.shooterNickname)) return;
            if (!string.IsNullOrEmpty(p.hitNickname))
                Console.WriteLine($"[Missile] HIT rejected ({reason}) id={p.missileId} hit={p.hitNickname}");
            p.hitNickname = ""; p.damage = 0f;
        }
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

    // 기만체 사출 — 본인 기체(AI 는 호스트)만 보고 가능, 방 전체에 중계 (발신자는 클라이언트에서 무시)
    public static void HandleCountermeasure(ClientSession session, string json)
    {
        var room = session.player?.room;
        if (room == null) return;
        var p = JsonConvert.DeserializeObject<CountermeasurePacket>(json);
        if (p == null || !HitValidation.CanActAs(room, session.player!, p.ownerNickname)) return;
        if (p.cmType < 0 || p.cmType > 1) return;
        room.Broadcast(p);
    }
}
