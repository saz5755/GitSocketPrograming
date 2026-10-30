using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class PacketHandler
{
    static Dictionary<PacketType, Action<ClientSession, string>> handlers = new();
    
    static Dictionary<PacketType, Action<IPEndPoint, string>> udpHandlers = new();

    public static void Init()
    {
        handlers[PacketType.LOGIN]
            = LoginHandler.Handle;

        handlers[PacketType.CHAT]
            = ChatHandler.Handle;
        
        handlers[PacketType.ENTER_ROOM]
            = EnterRoomHandler.Handle;

        handlers[PacketType.ROOM_LIST_REQUEST]
            = RoomListHandler.Handle;

        handlers[PacketType.LEAVE_ROOM]
            = LeaveRoomHandler.Handle;

        handlers[PacketType.CREATE_ROOM]
            = CreateRoomHandler.Handle;

        handlers[PacketType.MISSILE_SPAWN]
            = MissileHandler.HandleSpawn;
        handlers[PacketType.MISSILE_DESTROY]
            = MissileHandler.HandleDestroy;
        handlers[PacketType.MISSILE_WARN]
            = MissileHandler.HandleWarn;
        handlers[PacketType.COUNTERMEASURE]
            = MissileHandler.HandleCountermeasure;
        handlers[PacketType.ROOM_SETTING]
            = RoomSettingHandler.Handle;

        // TCP Move는 제거 예정
        /*handlers[PacketType.MOVE]
            = MoveHandler.Handle;*/
        
        udpHandlers[PacketType.UDP_CONNECT]
            = UdpConnectHandler.Handle;

        udpHandlers[PacketType.MOVE]
            = MoveHandler.HandleUDP;

        udpHandlers[PacketType.MISSILE_MOVE]
            = MissileMoveHandler.HandleUDP;

        handlers[PacketType.GUN_HIT]
            = GunHandler.HandleHit;
        udpHandlers[PacketType.GUN_FIRE]
            = GunHandler.HandleFire;

        handlers[PacketType.QUEST_UPDATE]
            = QuestHandler.Handle;

        handlers[PacketType.COCKPIT_STATE]
            = CockpitStateHandler.Handle;

        handlers[PacketType.AI_SPAWN]   = AISpawnHandler.Handle;
        handlers[PacketType.AI_DESPAWN] = AISpawnHandler.HandleDespawn;
        udpHandlers[PacketType.AI_MOVE] = AIMoveHandler.HandleUDP;

        handlers[PacketType.BOARD_AIRCRAFT] = AircraftBoardHandler.HandleBoard;
        handlers[PacketType.LEAVE_AIRCRAFT] = AircraftBoardHandler.HandleLeave;
        handlers[PacketType.ARREST_EVENT]   = ArrestEventHandler.Handle;
        handlers[PacketType.PLAYER_KILLED]  = CombatHandler.HandleKilled;
        handlers[PacketType.AIRCRAFT_STATE] = CombatHandler.HandleAircraftState;
    }

    // 기본적으로 TCP Handle
    public static void Handle(ClientSession session, string json)
    {
        if(string.IsNullOrEmpty(json))
        {
            Console.WriteLine("Empty Packet");
            return;
        }

        Packet? packet = JsonConvert.DeserializeObject<Packet>(json);

        if(packet == null)
        {
            Console.WriteLine($"Invalid Packet : {json}");
            return;
        }

        if(handlers.ContainsKey(packet.type))
        {
            handlers[packet.type](session, json);
        }
    }
    
    // 바이너리 UDP (v4.3 N-1) — 고빈도 이동 패킷 3종
    public static void HandleUDPBinary(IPEndPoint remoteEP, byte[] data)
    {
        switch (BinaryProtocol.TypeOf(data))
        {
            case PacketType.MOVE:         MoveHandler.HandleBinary(remoteEP, data);        break;
            case PacketType.AI_MOVE:      AIMoveHandler.HandleBinary(remoteEP, data);      break;
            case PacketType.AI_MOVE_BATCH: AIMoveBatchHandler.HandleBinary(remoteEP, data); break;
            case PacketType.MISSILE_MOVE: MissileMoveHandler.HandleBinary(remoteEP, data); break;
        }
    }

    public static void HandleUDP(IPEndPoint remoteEP, string json)
    {
        Packet? packet = JsonConvert.DeserializeObject<Packet>(json);
        if (packet == null) return;

        if(udpHandlers.ContainsKey(packet.type))
        {
            udpHandlers[packet.type](remoteEP, json);
        }
    }
}