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

    static void DisconnectSession(ClientSession session)
    {
        if (session == null) return;

        try
        {
            if (session.player?.room != null)
                session.player.room.Leave(session.player);

            SessionManager.Remove(session);

            session.stream?.Close();
            session.client?.Close();

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
