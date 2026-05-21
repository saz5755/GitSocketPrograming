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
        
        // TCP Move는 제거 예정 
        /*handlers[PacketType.MOVE]
            = MoveHandler.Handle;*/
        
        udpHandlers[PacketType.UDP_CONNECT]
            = UdpConnectHandler.Handle;

        udpHandlers[PacketType.MOVE]
            = MoveHandler.HandleUDP;
    }

    // 기본적으로 TCP Handle
    public static void Handle(ClientSession session, string json)
    {
        if(string.IsNullOrEmpty(json))
        {
            Console.WriteLine("Empty Packet");
            return;
        }

        Packet packet = JsonConvert.DeserializeObject<Packet>(json);

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
    
    public static void HandleUDP(IPEndPoint remoteEP, string json)
    {
        Packet packet = JsonConvert.DeserializeObject<Packet>(json);

        if(udpHandlers.ContainsKey(packet.type))
        {
            udpHandlers[packet.type](remoteEP, json);
        }

        Console.WriteLine($"UDP : {json}");
    }
}