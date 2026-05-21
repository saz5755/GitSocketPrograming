// Client → Server (UDP): 클라이언트 현재 위치 + 전체 회전 전송
public class MovePacket : Packet
{
    public int tick;

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
