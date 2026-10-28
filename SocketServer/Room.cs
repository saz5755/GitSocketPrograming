public class Room
{
    public int roomId;
    public string roomName = "Unknown Zone";
    public int maxPlayers = 8;
    public string hostNickname = string.Empty;
    /// <summary>방 모드 — 생성 시 정해지고 바뀌지 않는다 (RoomModes)</summary>
    public string mode = RoomModes.Combat;
    public bool   IsCombat => mode == RoomModes.Combat;

    List<Player> players = new();

    // 빈 방이 된 시각 (생성 직후 포함). 입장자가 있으면 null.
    // 생성 후 아무도 들어오지 않거나(씬 로딩 중 종료/이탈) 입장 직후 이탈한 경우의 유령 방 정리용.
    public DateTime? emptySinceUtc = DateTime.UtcNow;
    readonly object locker = new();

    readonly Dictionary<string, AircraftEntry> _aircraftPool = new();
    readonly object _poolLock = new();

    // 방 단위 전과 (입장 시 0 으로 생성, 퇴장 시 제거)
    readonly Dictionary<string, ScoreEntry> _scores = new();
    readonly object _scoreLock = new();

    // ── 피격 검증용 상태 (HitValidation) ────────────────────────────────────
    readonly Dictionary<string, PoseHistory> _history = new();
    readonly Dictionary<string, (string shooter, DateTime at)> _missiles = new();
    readonly Dictionary<string, Queue<long>> _gunHits = new();
    readonly object _validLock = new();

    public void RecordPose(string nickname, float x, float y, float z)
    {
        PoseHistory? h;
        lock (_validLock)
            if (!_history.TryGetValue(nickname, out h)) _history[nickname] = h = new PoseHistory();
        h.Add(x, y, z);
    }

    public PoseHistory? History(string nickname)
    {
        lock (_validLock) return _history.TryGetValue(nickname, out var h) ? h : null;
    }

    public void RegisterMissile(string id, string shooter)
    {
        lock (_validLock)
        {
            _missiles[id] = (shooter, DateTime.UtcNow);
            // 소멸 보고가 유실된 오래된 항목 정리
            if (_missiles.Count > 256)
                foreach (var k in _missiles.Where(kv => DateTime.UtcNow - kv.Value.at > TimeSpan.FromMinutes(2)).Select(kv => kv.Key).ToList())
                    _missiles.Remove(k);
        }
    }

    /// <summary>미사일 소멸 — 한 번만 성공한다 (같은 미사일로 두 번 명중 보고 차단)</summary>
    public bool TakeMissile(string id, out string shooter, out DateTime spawnedAt)
    {
        lock (_validLock)
        {
            if (_missiles.Remove(id, out var e)) { shooter = e.shooter; spawnedAt = e.at; return true; }
        }
        shooter = ""; spawnedAt = default;
        return false;
    }

    /// <summary>초당 명중 수 상한 — 넘으면 false</summary>
    public bool CountGunHit(string shooter, int maxPerSecond)
    {
        long now = DateTime.UtcNow.Ticks, since = now - TimeSpan.TicksPerSecond;
        lock (_validLock)
        {
            if (!_gunHits.TryGetValue(shooter, out var q)) _gunHits[shooter] = q = new Queue<long>();
            while (q.Count > 0 && q.Peek() < since) q.Dequeue();
            if (q.Count >= maxPerSecond) return false;
            q.Enqueue(now);
            return true;
        }
    }

    // ── 전과 ────────────────────────────────────────────────────────────────

    public void RecordKill(string victim, string killer, bool victimIsBot)
    {
        lock (_scoreLock)
        {
            if (!victimIsBot && _scores.TryGetValue(victim, out var v)) v.deaths++;
            if (!string.IsNullOrEmpty(killer) && killer != victim && _scores.TryGetValue(killer, out var k))
            {
                if (victimIsBot) k.aiKills++;
                else             k.kills++;
            }
        }
    }

    public void BroadcastScoreboard()
    {
        var packet = new ScoreboardPacket { type = PacketType.SCOREBOARD };
        lock (_scoreLock)
            foreach (var e in _scores.Values)
                packet.entries.Add(new ScoreEntry { nickname = e.nickname, kills = e.kills, aiKills = e.aiKills, deaths = e.deaths });
        Broadcast(packet);
    }

    // ── 항공기 풀 ────────────────────────────────────────────────────────────

    public void UpsertAircraft(AircraftEntry e)
    {
        lock (_poolLock) _aircraftPool[e.nickname] = e;
    }

    public void RemoveAircraft(string nickname)
    {
        lock (_poolLock) _aircraftPool.Remove(nickname);
    }

    // 포지션만 갱신 (엔트리 없으면 무시하고 false)
    public bool UpdateAircraftPose(string nickname,
        float px, float py, float pz,
        float rx, float ry, float rz,
        float speed, bool isFlying)
    {
        lock (_poolLock)
        {
            if (!_aircraftPool.TryGetValue(nickname, out var e)) return false;
            e.posX = px; e.posY = py; e.posZ = pz;
            e.rotX = rx; e.rotY = ry; e.rotZ = rz;
            e.speed   = speed;
            e.isFlying = isFlying;
            return true;
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
            emptySinceUtc = null;
        }

        player.room = this;

        if (isFirst)
            hostNickname = player.nickname;

        lock (_scoreLock) _scores[player.nickname] = new ScoreEntry { nickname = player.nickname };

        BroadcastSystem($"{player.nickname} joined");
        BroadcastScoreboard();
    }

    public void Leave(Player player, bool disconnected = false)
    {
        bool wasLast;
        bool wasHost = player.nickname == hostNickname;
        lock (locker)
        {
            players.Remove(player);
            wasLast = players.Count == 0;
            if (wasLast) emptySinceUtc = DateTime.UtcNow;
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

        lock (_scoreLock) _scores.Remove(player.nickname);
        if (!wasLast) BroadcastScoreboard();

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

        // 마지막 플레이어가 나가면 룸 삭제 (로비 목록 자동 갱신).
        // 연결이 끊긴 경우는 재접속 유예를 위해 남겨 두고 빈 방 정리기(SweepEmptyRooms)에 맡긴다.
        if (wasLast && !disconnected)
            RoomManager.DeleteRoom(roomId);
        else if (wasLast)
            RoomManager.BroadcastRoomListToLobby();
    }

    public void Broadcast(object packet)
    {
        List<Player> copied;
        lock (locker)
        {
            copied = new List<Player>(players);
        }

        byte[]? frame = null;   // 직렬화 1회 — 수신자마다 재직렬화하지 않는다
        foreach (Player player in copied)
        {
            if (player?.session == null) continue;
            ServerSender.SendFrame(player.session, frame ??= ServerSender.Frame(packet));
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

/// <summary>방 모드 상수 — 클라이언트 RoomModeSO 이름과 같다</summary>
public static class RoomModes
{
    public const string Airshow = "AIRSHOW";
    public const string Combat  = "COMBAT";

    public static string Normalize(string? m)
        => string.Equals(m, Airshow, StringComparison.OrdinalIgnoreCase) ? Airshow : Combat;
}
