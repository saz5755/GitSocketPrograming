using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;

class Program
{
    static TcpListener server = null!;
    public static UdpClient udpServer = null!;

    static void Main()
    {
        PacketHandler.Init();
        RoomManager.Init();
        ServerTick.Start();

        server = new TcpListener(IPAddress.Any, 5000);
        server.Start();
        Console.WriteLine("[TCP] Server started on port 5000");

        udpServer = new UdpClient(6000);
        // Windows: 끊긴 클라이언트로 보낸 UDP가 ICMP Port Unreachable을 받으면 다음 Receive가
        // ConnectionReset(10054)으로 실패한다. 수신 루프가 에러 로그를 쏟지 않도록 이 동작을 끈다.
        if (OperatingSystem.IsWindows())
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            udpServer.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        }
        Thread udpThread = new Thread(UdpReceiveLoop) { IsBackground = true };
        udpThread.Start();
        Console.WriteLine("[UDP] Server started on port 6000");

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();

            ClientSession session = new()
            {
                client = client,
                stream = client.GetStream()
            }; 

            SessionManager.Add(session);
            session.StartWriter();

            Thread thread = new Thread(HandleClient) { IsBackground = true };
            thread.Start(session);
        }
    }

    static void UdpReceiveLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        while (true)
        {
            try
            {
                byte[] data = udpServer.Receive(ref remoteEP);
                if (BinaryProtocol.IsBinary(data))
                {
                    PacketHandler.HandleUDPBinary(remoteEP, data);
                    continue;
                }
                string json = Encoding.UTF8.GetString(data);
                PacketHandler.HandleUDP(remoteEP, json);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[UDP] Recv error: {e.Message}");
            }
        }
    }

    static void HandleClient(object? obj)
    {
        ClientSession session = (ClientSession)obj!;
        NetworkStream stream = session.stream;
        byte[] lengthBuffer = new byte[4];

        while (true)
        {
            try
            {
                ReadFull(stream, lengthBuffer, 4);
                int length = BitConverter.ToInt32(lengthBuffer, 0);

                if (length <= 0 || length > 1024 * 32)
                    throw new Exception($"Invalid packet size: {length}");

                byte[] dataBuffer = new byte[length];
                ReadFull(stream, dataBuffer, length);

                string json = Encoding.UTF8.GetString(dataBuffer);

                try
                {
                    PacketHandler.Handle(session, json);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[TCP] Packet handle error: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[TCP] Client error: {e.Message}");
                DisconnectSession(session);
                break;
            }
        }
    }

    public static void DisconnectSession(ClientSession session)
    {
        if (session == null) return;

        try
        {
            // 세션 먼저 제거 — BroadcastRoomListToLobby가 이 세션에 전송하지 않도록
            if (!SessionManager.Remove(session)) return;   // 이미 처리됨 (재로그인 인수 등)
            session.Close();

            // 연결 끊김은 명시적 퇴장과 달리, 마지막 인원이어도 방을 바로 지우지 않는다 —
            // 재접속 유예 동안 같은 방으로 돌아올 수 있게 (빈 방 정리기가 유예 후 삭제)
            if (session.player?.room != null)
                session.player.room.Leave(session.player, disconnected: true);

            Console.WriteLine($"[TCP] Disconnected: {session.player?.nickname}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[TCP] Disconnect error: {e.Message}");
        }
    }

    static void ReadFull(NetworkStream stream, byte[] buffer, int size)
    {
        int offset = 0;
        while (offset < size)
        {
            int read = stream.Read(buffer, offset, size - offset);
            if (read <= 0)
                throw new Exception("Connection closed");
            offset += read;
        }
    }
}
