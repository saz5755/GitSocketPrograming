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

        // 세션 토큰이 맞아야 등록 — 닉네임만 알면 남의 위치 패킷을 보낼 수 있던 구멍(B-4) 차단
        if (string.IsNullOrEmpty(session.sessionToken) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(packet.token ?? ""),
                System.Text.Encoding.UTF8.GetBytes(session.sessionToken)))
        {
            Console.WriteLine($"UDP Connect rejected (bad token) : {packet.nickname} from {remoteEP}");
            return;
        }

        bool changed = session.udpEndPoint == null || !session.udpEndPoint.Equals(remoteEP);
        session.udpEndPoint = remoteEP;
        if (!changed) return;   // keepalive — 로그 생략

        Console.WriteLine(
            $"UDP Connected : {packet.nickname}");
    }
}