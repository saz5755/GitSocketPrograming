// Server → Client (TCP): 플레이어 퇴장 통보
public class DespawnPacket : Packet
{
    public string nickname;
    public bool   wasFlying;
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
}
