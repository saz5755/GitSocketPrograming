using System.Text;
using Newtonsoft.Json;

class ServerSender
{
    public static void SendPacket(ClientSession session, object packet)
    {
        try
        {
            string json =
                JsonConvert.SerializeObject(packet);

            byte[] jsonBytes =
                Encoding.UTF8.GetBytes(json);

            byte[] length =
                BitConverter.GetBytes(jsonBytes.Length);

            session.stream.Write(length, 0, 4);
            session.stream.Write(jsonBytes, 0, jsonBytes.Length);
            
            // Console.WriteLine($"SEND TO : {session.player?.nickname}");
        }
        catch(Exception e)
        {
            Console.WriteLine($"Send Error : {e}");
            Console.WriteLine(
                $"SEND ERROR : {session.player?.nickname}");
        }
        
    }

    public static void Broadcast(object packet)
    {
        List<ClientSession> disconnected = new();

        foreach (ClientSession session in SessionManager.GetSessions())
        {
            try
            {
                SendPacket(session, packet);
            }
            catch
            {
                disconnected.Add(session);
            }
        }

        foreach (var s in disconnected)
        {
            s.stream?.Close();
            s.client?.Close();
            
            SessionManager.Remove(s);
        }
    }

    public static void BroadcastSystemMessage(string msg)
    {
        ChatPacket packet = new();

        packet.type = PacketType.SYSTEM;
        packet.nickname = "SYSTEM";
        packet.message = msg;

        Broadcast(packet);
    }

    public static void SendLoginResult(
        ClientSession session,
        bool success,
        string msg)
    {
        LoginResultPacket packet = new();

        packet.type = PacketType.LOGIN_RESULT;
        packet.success = success;
        packet.message = msg;

        SendPacket(session, packet);
    }
}