using Newtonsoft.Json;

// ARREST_EVENT (TCP): 게스트의 착함 이벤트를 방 전체에 중계 — 호스트가 에스코트 AI 착함을 처리한다
// eventType 0=와이어 체결(에스코트 자유비행), 1=하차(와이어 경로 있음), 2=하차(경로 없음)
class ArrestEventPacket : Packet
{
    public string nickname = "";
    public int    eventType;
    public float  approachDirX, approachDirY, approachDirZ;
    public float  wirePosX, wirePosY, wirePosZ;
}

class ArrestEventHandler
{
    public static void Handle(ClientSession session, string json)
    {
        var player = session.player;
        if (player?.room == null) return;

        var p = JsonConvert.DeserializeObject<ArrestEventPacket>(json);
        if (p == null) return;

        // 발신자 위조 방지 — 닉네임은 세션 기준
        p.nickname = player.nickname;
        foreach (Player target in player.room.GetPlayers())
        {
            if (target == player || target.session == null) continue;
            ServerSender.SendPacket(target.session, p);
        }
        Console.WriteLine($"[Arrest] {player.nickname} event={p.eventType}");
    }
}
