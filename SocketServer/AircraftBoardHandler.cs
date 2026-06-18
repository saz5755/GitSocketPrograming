using Newtonsoft.Json;

class AircraftBoardHandler
{
    public static void HandleBoard(ClientSession session, string json)
    {
        var room = session.player?.room;
        if (room == null) return;

        var p = JsonConvert.DeserializeObject<AircraftBoardPacket>(json);
        if (p == null) return;

        session.player.boardedAircraftId = p.aircraftId;
        p.type     = PacketType.BOARD_AIRCRAFT;
        p.nickname = session.player.nickname;
        room.Broadcast(p);

        Console.WriteLine($"[Aircraft] {session.player.nickname} boarded aircraft {p.aircraftId}");
    }

    public static void HandleLeave(ClientSession session, string json)
    {
        var room = session.player?.room;
        if (room == null) return;

        int prevId = session.player.boardedAircraftId;
        session.player.boardedAircraftId = -1;

        room.Broadcast(new AircraftBoardPacket
        {
            type       = PacketType.LEAVE_AIRCRAFT,
            nickname   = session.player.nickname,
            aircraftId = prevId
        });

        Console.WriteLine($"[Aircraft] {session.player.nickname} left aircraft {prevId}");
    }
}
