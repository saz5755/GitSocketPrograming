public class Room
{
    public int roomId;
    public string roomName = "Unknown Zone";
    public int maxPlayers = 8;
    public string hostNickname = string.Empty;

    List<Player> players = new();
    readonly object locker = new();

    readonly Dictionary<string, AircraftEntry> _aircraftPool = new();
    readonly object _poolLock = new();

    // ── 항공기 풀 ────────────────────────────────────────────────────────────

    public void UpsertAircraft(AircraftEntry e)
    {
        lock (_poolLock) _aircraftPool[e.nickname] = e;
    }

    public void RemoveAircraft(string nickname)
    {
        lock (_poolLock) _aircraftPool.Remove(nickname);
    }

    // AI_MOVE 수신 시 포지션만 갱신 (엔트리 없으면 무시)
    public void UpdateAircraftPose(string nickname,
        float px, float py, float pz,
        float rx, float ry, float rz,
        float speed, bool isFlying)
    {
        lock (_poolLock)
        {
            if (_aircraftPool.TryGetValue(nickname, out var e))
            {
                e.posX = px; e.posY = py; e.posZ = pz;
                e.rotX = rx; e.rotY = ry; e.rotZ = rz;
                e.speed   = speed;
                e.isFlying = isFlying;
            }
        }
    }

    public List<AircraftEntry> GetAircraftPool()
    {
        lock (_poolLock) return new List<AircraftEntry>(_aircraftPool.Values);
    }

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
        bool isFirst;
        lock (locker)
        {
            isFirst = players.Count == 0;
            players.Add(player);
        }

        player.room = this;

        if (isFirst)
            hostNickname = player.nickname;

        BroadcastSystem($"{player.nickname} joined");
    }

    public void Leave(Player player)
    {
        bool wasLast;
        bool wasHost = player.nickname == hostNickname;
        lock (locker)
        {
            players.Remove(player);
            wasLast = players.Count == 0;
        }

        player.room = null;

        Console.WriteLine($"[Room] {player.nickname} left room {roomId}");

        // 항공기 풀에서 제거 — 재입장 시 신규 입장자가 구 위치를 받지 않도록
        RemoveAircraft(player.nickname);

        DespawnPacket despawn = new()
        {
            type      = PacketType.DESPAWN,
            nickname  = player.nickname,
            wasFlying = player.isFlying,
            posX = player.posX, posY = player.posY, posZ = player.posZ,
            rotX = player.rotX, rotY = player.rotY, rotZ = player.rotZ
        };
        Broadcast(despawn);

        BroadcastSystem($"{player.nickname} left");

        // 호스트가 나갔고 남은 플레이어가 있으면 다음 플레이어에게 호스트 승계
        if (wasHost && !wasLast)
        {
            List<Player> remaining;
            lock (locker) { remaining = new List<Player>(players); }
            if (remaining.Count > 0)
            {
                hostNickname = remaining[0].nickname;
                Broadcast(new HostChangePacket
                {
                    type            = PacketType.HOST_CHANGE,
                    newHostNickname = hostNickname
                });
                Console.WriteLine($"[Room] Host changed to {hostNickname} in room {roomId}");
            }
        }

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
