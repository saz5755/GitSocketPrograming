using Newtonsoft.Json;

class EnterRoomHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (!session.isLogin) return;

        EnterRoomPacket packet = JsonConvert.DeserializeObject<EnterRoomPacket>(json);
        Room room = RoomManager.GetRoom(packet.roomId);

        if (room == null)
        {
            Console.WriteLine($"[Room] Not found: {packet.roomId}");
            return;
        }

        if (session.player.room != null)
            session.player.room.Leave(session.player);

        // 재입장 시 틱 카운터 리셋 (클라이언트 tick은 씬 재로드마다 0부터 시작)
        session.player.lastProcessedTick = 0;

        // 초기 스폰 위치 설정
        session.player.posX = 0;
        session.player.posY = 0;
        session.player.posZ = 0;
        session.player.rotX = 0;
        session.player.rotY = 0;
        session.player.rotZ = 0;

        // 1. 기존 플레이어 목록을 새 클라이언트에게 전송 (입장 전에 전송해야 자신 제외)
        foreach (Player player in room.GetPlayers())
        {
            SpawnPacket spawn = new()
            {
                type = PacketType.SPAWN,
                nickname = player.nickname,
                x = player.posX,
                y = player.posY,
                z = player.posZ,
                rotX = player.rotX,
                rotY = player.rotY,
                rotZ = player.rotZ,
                isMove = player.isMove
            };
            ServerSender.SendPacket(session, spawn);
        }

        // 2. 방 입장 처리
        room.Enter(session.player);

        // 3. 새 플레이어를 방 전체(자신 포함)에게 브로드캐스트
        SpawnPacket mySpawn = new()
        {
            type = PacketType.SPAWN,
            nickname = session.player.nickname,
            x = 0,
            y = 0,
            z = 0,
            rotX = 0,
            rotY = 0,
            rotZ = 0,
            isMove = false
        };
        room.Broadcast(mySpawn);

        Console.WriteLine($"[Room] {session.player.nickname} entered room {packet.roomId}");
    }
}
