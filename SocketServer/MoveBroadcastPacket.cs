// Server → Client (UDP): 서버가 다른 플레이어들에게 브로드캐스트
public class MoveBroadcastPacket : Packet
{
    public int tick;
    public string nickname = string.Empty;

    // World Position
    public float posX;
    public float posY;
    public float posZ;

    // World Rotation (Euler)
    public float rotX;
    public float rotY;
    public float rotZ;

    public bool isMove;
}
