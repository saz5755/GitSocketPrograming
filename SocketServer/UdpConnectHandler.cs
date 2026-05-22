using System.Net;
using Newtonsoft.Json;

class UdpConnectHandler
{
    public static void Handle(IPEndPoint remoteEP, string json)
    {
        UdpConnectPacket? packet =
            JsonConvert.DeserializeObject<UdpConnectPacket>(json);
        if (packet == null) return;

        ClientSession? session =
            SessionManager.FindByNickname(packet.nickname);

        if(session == null)
        {
            Console.WriteLine(
                $"UDP Connect Failed : {packet.nickname}");
            return;
        }

        session.udpEndPoint = remoteEP;

        Console.WriteLine(
            $"UDP Connected : {packet.nickname}");
    }
}