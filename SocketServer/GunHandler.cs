using System.Net;
using System.Text;
using Newtonsoft.Json;

class GunHandler
{
    // UDP: GUN_FIRE → relay raw JSON to all other room members
    public static void HandleFire(IPEndPoint remoteEP, string json)
    {
        ClientSession? session = SessionManager.FindByUDP(remoteEP);
        if (session?.player?.room == null) return;

        var p = JsonConvert.DeserializeObject<GunFirePacket>(json);
        if (p == null || string.IsNullOrEmpty(p.shooterNickname)) return;

        byte[] data = Encoding.UTF8.GetBytes(json);
        foreach (Player target in session.player.room.GetPlayers())
        {
            if (target == session.player) continue;
            if (target.session?.udpEndPoint == null) continue;
            try { Program.udpServer.Send(data, data.Length, target.session.udpEndPoint); }
            catch { }
        }
    }

    // TCP: GUN_HIT → broadcast to entire room (shooter + target + spectators)
    public static void HandleHit(ClientSession session, string json)
    {
        if (session.player?.room == null) return;
        var p = JsonConvert.DeserializeObject<GunHitPacket>(json);
        if (p == null) return;
        Console.WriteLine($"[Gun] HIT  shooter={p.shooterNickname} target={p.targetNickname}");
        session.player.room.Broadcast(p);
    }
}
