using System.Linq;

class RoomListHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (!session.isLogin) return;

        var rooms = RoomManager.GetAllRooms()
            .Select(r => new RoomInfo
            {
                roomId      = r.roomId,
                roomName    = r.roomName,
                playerCount = r.GetPlayerCount(),
                maxPlayers  = r.maxPlayers
            })
            .ToList();

        ServerSender.SendPacket(session, new RoomListResultPacket
        {
            type  = PacketType.ROOM_LIST_RESULT,
            rooms = rooms
        });
    }
}
