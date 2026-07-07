using Newtonsoft.Json;

class AISpawnHandler
{
    // AI_SPAWN: 방 전체 브로드캐스트 (호스트 포함)
    public static void Handle(ClientSession session, string json)
    {
        var room = session.player?.room;
        if (room == null) return;

        var packet = JsonConvert.DeserializeObject<AISpawnPacket>(json);
        if (packet == null) return;

        room.Broadcast(packet);
        room.UpsertAircraft(new AircraftEntry
        {
            nickname = packet.nickname,
            posX = packet.posX, posY = packet.posY, posZ = packet.posZ,
            rotX = packet.rotX, rotY = packet.rotY, rotZ = packet.rotZ,
            aiType   = packet.aiType,
            isFlying = true
        });
        Console.WriteLine($"[AI] Spawn: {packet.nickname}  type={packet.aiType}");
    }

    // AI_DESPAWN: 방 전체 브로드캐스트
    public static void HandleDespawn(ClientSession session, string json)
    {
        var room = session.player?.room;
        if (room == null) return;

        var packet = JsonConvert.DeserializeObject<DespawnPacket>(json);
        if (packet == null) return;

        room.Broadcast(packet);
        room.RemoveAircraft(packet.nickname);
        Console.WriteLine($"[AI] Despawn: {packet.nickname}");
    }
}
