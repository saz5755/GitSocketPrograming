class LeaveRoomHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (!session.isLogin || session.player?.room == null) return;
        session.player.room.Leave(session.player);
        Console.WriteLine($"[Room] {session.player.nickname} left room via LEAVE_ROOM");
    }
}
