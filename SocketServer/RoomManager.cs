using System.Linq;

public class RoomManager
{
    static readonly Dictionary<int, Room> _rooms  = new();
    static readonly object                _lock   = new();
    static int _nextId = 1;

    // 서버 시작 시 고정 룸 없음 — 플레이어가 직접 생성
    public static void Init() { }

    public static IEnumerable<Room> GetAllRooms()
    {
        lock (_lock) { return new List<Room>(_rooms.Values); }
    }

    public static Room? GetRoom(int roomId)
    {
        lock (_lock) { _rooms.TryGetValue(roomId, out var r); return r; }
    }

    // 플레이어 요청으로 룸 동적 생성
    public static Room CreateRoom(string roomName, int maxPlayers, string mode = RoomModes.Combat)
    {
        lock (_lock)
        {
            int id = _nextId++;
            string name = string.IsNullOrWhiteSpace(roomName)
                ? $"Zone {id:D2}"
                : roomName.Trim();
            int cap = Math.Clamp(maxPlayers <= 0 ? 8 : maxPlayers, 2, 16);

            var room = new Room { roomId = id, roomName = name, maxPlayers = cap, mode = RoomModes.Normalize(mode) };
            _rooms.Add(id, room);
            Console.WriteLine($"[Room] Created #{id} \"{name}\" (max={cap}, mode={room.mode})");
            return room;
        }
    }

    // 마지막 플레이어 퇴장 시 Room.Leave()에서 호출
    public static void DeleteRoom(int roomId)
    {
        bool removed;
        lock (_lock) { removed = _rooms.Remove(roomId); }
        if (removed)
        {
            Console.WriteLine($"[Room] Deleted #{roomId}");
            BroadcastRoomListToLobby();
        }
    }

    // 생성 후 GracePeriod 동안 아무도 입장하지 않은(또는 비어 버린) 방을 삭제.
    // 출격 연출(~8초) + 씬 로딩 시간을 넉넉히 기다린 뒤 정리한다.
    static readonly TimeSpan EmptyRoomGrace = TimeSpan.FromSeconds(20);

    public static void SweepEmptyRooms()
    {
        var now = DateTime.UtcNow;
        List<int> stale = new();
        lock (_lock)
        {
            foreach (var r in _rooms.Values)
                if (r.GetPlayerCount() == 0 && r.emptySinceUtc.HasValue && now - r.emptySinceUtc.Value > EmptyRoomGrace)
                    stale.Add(r.roomId);
        }
        foreach (var id in stale)
        {
            Console.WriteLine($"[Room] Sweeping empty room #{id}");
            DeleteRoom(id);
        }
    }

    // 룸 목록이 바뀔 때마다 로비 클라이언트(룸 미참여)에게 자동 푸시
    public static void BroadcastRoomListToLobby()
    {
        var packet = BuildRoomListPacket();
        foreach (var session in SessionManager.GetSessions())
        {
            if (session.isLogin && session.player?.room == null)
            {
                try { ServerSender.SendPacket(session, packet); }
                catch { }
            }
        }
    }

    static RoomListResultPacket BuildRoomListPacket()
    {
        return new RoomListResultPacket
        {
            type  = PacketType.ROOM_LIST_RESULT,
            rooms = GetAllRooms().Select(r => new RoomInfo
            {
                roomId      = r.roomId,
                roomName    = r.roomName,
                playerCount = r.GetPlayerCount(),
                maxPlayers  = r.maxPlayers,
                mode        = r.mode
            }).ToList()
        };
    }
}
