using Newtonsoft.Json;

class ChatHandler
{
    public static void Handle(ClientSession session, string json)
    {
        if (session.isLogin == false)
            return;
        
        if (session.player.room == null)
            return;

        ChatPacket packet = JsonConvert.DeserializeObject<ChatPacket>(json);
        
        packet.nickname = session.player.nickname;

        Console.WriteLine($"[{packet.nickname}] {packet.message}");

        session.player.room.Broadcast(packet);
    }
}