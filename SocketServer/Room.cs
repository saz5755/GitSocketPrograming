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
        lock (locker)
        {
            players.Remove(player);
        }

        player.room = null;

        Console.WriteLine($"[Room] {player.nickname} left room {roomId}");

        // 남은 플레이어들에게 DESPAWN 통보
        DespawnPacket despawn = new()
        {
            type = PacketType.DESPAWN,
            nickname = player.nickname
        };
        Broadcast(despawn);

        BroadcastSystem($"{player.nickname} left");
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
