// Server → Client (TCP): 플레이어가 방에서 나갔을 때 통보
public class DespawnPacket : Packet
{
    public string nickname  = string.Empty;
    public bool   wasFlying = false;
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
}
