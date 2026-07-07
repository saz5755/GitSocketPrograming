using Newtonsoft.Json;

class AircraftBoardHandler
{
    public static void HandleBoard(ClientSession session, string json)
    {
        var player = session.player;
        if (player?.room == null) return;

        var p = JsonConvert.DeserializeObject<AircraftBoardPacket>(json);
        if (p == null) return;

        player.boardedAircraftId = p.aircraftId;
        p.type     = PacketType.BOARD_AIRCRAFT;
        p.nickname = player.nickname;
        player.room.Broadcast(p);

        Console.WriteLine($"[Aircraft] {player.nickname} boarded aircraft {p.aircraftId}");
    }

    public static void HandleLeave(ClientSession session, string json)
    {
        var player = session.player;
        if (player?.room == null) return;

        int prevId = player.boardedAircraftId;
        player.boardedAircraftId = -1;

        player.room.Broadcast(new AircraftBoardPacket
        {
            type       = PacketType.LEAVE_AIRCRAFT,
            nickname   = player.nickname,
            aircraftId = prevId
        });

        Console.WriteLine($"[Aircraft] {player.nickname} left aircraft {prevId}");
    }
}
