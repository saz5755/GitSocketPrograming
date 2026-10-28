using System.Text;
using Newtonsoft.Json;

class ServerSender
{
    // 송신은 세션별 비동기 큐에 프레임을 넣고 바로 돌아온다 (ClientSession.Enqueue, v4.3 N-3)
    public static void SendPacket(ClientSession session, object packet)
        => session.Enqueue(Frame(packet));

    /// <summary>길이 프리픽스(4B) + UTF-8 JSON 한 덩어리 — 브로드캐스트는 한 번만 만들어 수신자마다 재사용한다</summary>
    public static byte[] Frame(object packet)
    {
        byte[] json  = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
        byte[] frame = new byte[4 + json.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), json.Length);
        Buffer.BlockCopy(json, 0, frame, 4, json.Length);
        return frame;
    }

    public static void SendFrame(ClientSession session, byte[] frame) => session.Enqueue(frame);

    public static void Broadcast(object packet)
    {
        byte[] frame = Frame(packet);
        foreach (ClientSession session in SessionManager.GetSessions())
            session.Enqueue(frame);
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
        packet.sessionToken = success ? session.sessionToken : string.Empty;

        SendPacket(session, packet);
    }
}
