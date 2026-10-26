using System.Net;
using System.Text;
using Newtonsoft.Json;

class UdpSender
{
    public static void Send(IPEndPoint endPoint, object packet)
    {
        if(endPoint == null)
        {
            Console.WriteLine("UDP EndPoint NULL");

            return;
        }

        Send(endPoint, Serialize(packet));
    }

    public static byte[] Serialize(object packet)
        => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));

    // 이미 직렬화된 바이트 전송 — 브로드캐스트에서 수신자마다 재직렬화하지 않도록
    public static void Send(IPEndPoint endPoint, byte[] data)
    {
        try { Program.udpServer.Send(data, data.Length, endPoint); }
        catch (Exception e) { Console.WriteLine($"[UDP] Send error {endPoint}: {e.Message}"); }
    }

    // 방 안의 sender 제외 전원에게 전송. UDP 미등록 대상은 TCP로 fallback.
    // JSON 직렬화는 한 번만 (기존: 수신자 수만큼 반복 → 틱마다 O(N²)).
    public static void BroadcastToRoom(Room room, Player? sender, object packet)
    {
        byte[]? data = null;
        foreach (Player target in room.GetPlayers())
        {
            if (target == sender) continue;
            if (target.session == null) continue;

            if (target.session.udpEndPoint != null)
                Send(target.session.udpEndPoint, data ??= Serialize(packet));
            else
                ServerSender.SendPacket(target.session, packet);
        }
    }
}
