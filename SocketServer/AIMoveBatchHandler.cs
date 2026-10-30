using System.Net;
using System.Text;
using Newtonsoft.Json;

/// <summary>
/// AI_MOVE_BATCH (v4.4) — 호스트가 한 틱의 AI 전원을 한 패킷으로 보낸다.
/// 서버는 발신자가 방장인지·항목이 전부 BOT_ 인지 검증하고 풀/피격 검증 위치만 갱신한 뒤
/// 바이너리 수신자에게는 받은 바이트를 그대로 중계한다 (재인코딩·할당 최소화).
/// JSON 수신자(테스트 봇 등)에게는 기존처럼 봇별 AI_MOVE JSON 으로 풀어서 보낸다.
/// </summary>
class AIMoveBatchHandler
{
    [ThreadStatic] static List<AIMovePacket>? t_list;

    public static void HandleBinary(IPEndPoint remoteEP, byte[] data)
    {
        var session = SessionManager.FindByUDP(remoteEP);
        var room = session?.player?.room;
        if (room == null) return;
        session!.binaryUdp = true;

        // AI 기체는 호스트만 움직일 수 있다
        if (session.player!.nickname != room.hostNickname) return;

        var list = t_list ??= new List<AIMovePacket>(16);
        list.Clear();
        if (!BinaryProtocol.DecodeAIBatch(data, list) || list.Count == 0) return;
        foreach (var p in list)
            if (!p.nickname.StartsWith("BOT_")) return;

        foreach (var p in list)
        {
            room.RecordPose(p.nickname, p.posX, p.posY, p.posZ);
            room.UpdateAircraftPose(p.nickname, p.posX, p.posY, p.posZ, p.rotX, p.rotY, p.rotZ, p.speed, true);
        }

        foreach (Player target in room.GetPlayers())
        {
            if (target == session.player || target.session?.udpEndPoint == null) continue;
            if (target.session.binaryUdp)
                UdpSender.Send(target.session.udpEndPoint, data);
            else
                foreach (var p in list)
                    UdpSender.Send(target.session.udpEndPoint, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(p)));
        }
    }
}
