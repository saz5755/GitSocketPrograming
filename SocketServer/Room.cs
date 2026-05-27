public class Room
{
    public int roomId;
    public string roomName = "Unknown Zone";
    public int maxPlayers = 8;

    List<Player> players = new();
    readonly object locker = new();

    public int GetPlayerCount()
    {
        lock (locker) { return players.Count; }
    }

    public List<Player> GetPlayers()
    {
        lock (locker)
        {
            return new List<Player>(players);
        }
    }

    public void Enter(Player player)
    {
        lock (locker)
        {
            players.Add(player);
        }

        player.room = this;

        BroadcastSystem($"{player.nickname} joined");
    }

    public void Leave(Player player)
    {
        bool wasLast;
        lock (locker)
        {
            players.Remove(player);
            wasLast = players.Count == 0;
        }

        player.room = null;

        Console.WriteLine($"[Room] {player.nickname} left room {roomId}");

        DespawnPacket despawn = new()
        {
            type = PacketType.DESPAWN,
            nickname = player.nickname
        };
        Broadcast(despawn);

        BroadcastSystem($"{player.nickname} left");

        // 마지막 플레이어가 나가면 룸 삭제 (로비 목록 자동 갱신)
        if (wasLast)
            RoomManager.DeleteRoom(roomId);
    }

    public void Broadcast(object packet)
    {
        List<Player> copied;
        lock (locker)
        {
            copied = new List<Player>(players);
        }

        foreach (Player player in copied)
        {
            if (player?.session == null) continue;
            ServerSender.SendPacket(player.session, packet);
        }
    }

    void BroadcastSystem(string msg)
    {
        ChatPacket packet = new()
        {
            type = PacketType.SYSTEM,
            nickname = "SYSTEM",
            message = msg
        };
        Broadcast(packet);
    }
}
