using Newtonsoft.Json;

class EnterRoomHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (!session.isLogin || session.player == null) return;

        EnterRoomPacket? packet = JsonConvert.DeserializeObject<EnterRoomPacket>(json);
        if (packet == null) return;

        Room? room = RoomManager.GetRoom(packet.roomId);

        // 존재하지 않는 룸
        if (room == null)
        {
            Console.WriteLine($"[Room] Not found: {packet.roomId}");
            ServerSender.SendPacket(session, new EnterRoomResultPacket
            {
                type         = PacketType.ENTER_ROOM_RESULT,
                success      = false,
                errorMessage = "Room does not exist"
            });
            return;
        }

        // 정원 초과
        if (room.GetPlayerCount() >= room.maxPlayers)
        {
            ServerSender.SendPacket(session, new EnterRoomResultPacket
            {
                type         = PacketType.ENTER_ROOM_RESULT,
                success      = false,
                errorMessage = "Room is full"
            });
            return;
        }

        // 다른 룸에 있으면 먼저 퇴장
        if (session.player.room != null)
            session.player.room.Leave(session.player);

        // 재입장 시 틱 카운터 리셋
        session.player.lastProcessedTick = 0;
        session.player.posX = session.player.posY = session.player.posZ = 0;
        session.player.rotX = session.player.rotY = session.player.rotZ = 0;

        // 1. 기존 플레이어 목록을 새 클라이언트에게 전송
        foreach (Player player in room.GetPlayers())
        {
            SpawnPacket spawn = new()
            {
                type     = PacketType.SPAWN,
                nickname = player.nickname,
                x        = player.posX,
                y        = player.posY,
                z        = player.posZ,
                rotX     = player.rotX,
                rotY     = player.rotY,
                rotZ     = player.rotZ,
                isMove   = player.isMove
            };
            ServerSender.SendPacket(session, spawn);
        }

        // 2. 입장 처리
        room.Enter(session.player);

        // 3. 새 플레이어 스폰을 룸 전체에 브로드캐스트
        SpawnPacket mySpawn = new()
        {
            type     = PacketType.SPAWN,
            nickname = session.player.nickname,
            x = 0, y = 0, z = 0,
            rotX = 0, rotY = 0, rotZ = 0,
            isMove   = false
        };
        room.Broadcast(mySpawn);

        // 4. 입장 성공 결과 전송
        ServerSender.SendPacket(session, new EnterRoomResultPacket
        {
            type    = PacketType.ENTER_ROOM_RESULT,
            success = true,
            roomId  = room.roomId
        });

        Console.WriteLine($"[Room] {session.player.nickname} entered room {packet.roomId}");
    }
}
