// Server → Client (TCP): 플레이어 퇴장 통보
public class DespawnPacket : Packet
{
    public string nickname;
}
