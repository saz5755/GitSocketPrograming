using Newtonsoft.Json;

class CreateRoomHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (!session.isLogin || session.player == null)
        {
            ServerSender.SendPacket(session, new CreateRoomResultPacket
            {
                type = PacketType.CREATE_ROOM_RESULT,
                success = false,
                errorMessage = "Not logged in"
            });
            return;
        }

        var packet = JsonConvert.DeserializeObject<CreateRoomPacket>(json);
        if (packet == null) return;

        string roomName    = string.IsNullOrWhiteSpace(packet.roomName)
            ? $"{session.player.nickname}'s Zone"
            : packet.roomName.Trim();
        int    maxPlayers  = packet.maxPlayers <= 0 ? 8 : packet.maxPlayers;

        var room = RoomManager.CreateRoom(roomName, maxPlayers);

        // 생성자에게 성공 응답 (roomId 전달)
        ServerSender.SendPacket(session, new CreateRoomResultPacket
        {
            type      = PacketType.CREATE_ROOM_RESULT,
            success   = true,
            roomId    = room.roomId,
            roomName  = room.roomName
        });

        // 로비 클라이언트들에게 갱신된 룸 목록 푸시
        RoomManager.BroadcastRoomListToLobby();

        Console.WriteLine($"[Room] {session.player.nickname} created room #{room.roomId} \"{room.roomName}\"");
    }
}
