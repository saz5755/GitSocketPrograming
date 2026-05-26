using System.Net;
using System.Text;
using Newtonsoft.Json;

class MissileMoveHandler
{
    public static void HandleUDP(IPEndPoint remoteEP, string json)
    {
        ClientSession? session = SessionManager.FindByUDP(remoteEP);
        if (session?.player?.room == null) return;

        // 파싱 검증 후 원본 json 바이트를 그대로 릴레이 (재직렬화 불필요)
        var p = JsonConvert.DeserializeObject<MissileMovePacket>(json);
        if (p == null || string.IsNullOrEmpty(p.missileId)) return;

        byte[] data = Encoding.UTF8.GetBytes(json);

        foreach (Player target in session.player.room.GetPlayers())
        {
            if (target == session.player) continue;
            if (target.session?.udpEndPoint == null) continue;
            try { Program.udpServer.Send(data, data.Length, target.session.udpEndPoint); }
            catch { }
        }
    }
}
