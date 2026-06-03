using Newtonsoft.Json;

class QuestHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (!session.isLogin || session.player?.room == null) return;

        var p = JsonConvert.DeserializeObject<QuestUpdatePacket>(json);
        if (p == null) return;

        p.nickname = session.player.nickname;
        session.player.room.Broadcast(p);
    }
}
