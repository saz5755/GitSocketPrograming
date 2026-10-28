using System.Net;
using System.Text;
using Newtonsoft.Json;

class MissileMoveHandler
{
    public static void HandleUDP(IPEndPoint remoteEP, string json)
    {
        ClientSession? session = SessionManager.FindByUDP(remoteEP);
        if (session?.player?.room == null) return;

        var p = JsonConvert.DeserializeObject<MissileMovePacket>(json);
        if (p == null || string.IsNullOrEmpty(p.missileId)) return;
        Relay(session, p, jsonBytes: Encoding.UTF8.GetBytes(json));
    }

    public static void HandleBinary(IPEndPoint remoteEP, byte[] data)
    {
        ClientSession? session = SessionManager.FindByUDP(remoteEP);
        if (session?.player?.room == null) return;
        session.binaryUdp = true;

        var p = BinaryProtocol.DecodeMissileMove(data);
        if (p == null || string.IsNullOrEmpty(p.missileId)) return;
        Relay(session, p, binBytes: data);
    }

    // 받은 원본은 같은 형식 수신자에게 그대로, 다른 형식이 필요하면 한 번만 변환
    static void Relay(ClientSession session, MissileMovePacket p, byte[]? jsonBytes = null, byte[]? binBytes = null)
    {
        foreach (Player target in session.player!.room!.GetPlayers())
        {
            if (target == session.player) continue;
            if (target.session?.udpEndPoint == null) continue;
            byte[] data = target.session.binaryUdp
                ? binBytes  ??= BinaryProtocol.EncodeMissileMove(p)
                : jsonBytes ??= Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(p));
            try { Program.udpServer.Send(data, data.Length, target.session.udpEndPoint); }
            catch { }
        }
    }
}
