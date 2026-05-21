public class SpawnPacket : Packet
{
    public string nickname;

    // World Position
    public float x;
    public float y;
    public float z;

    // World Rotation (Euler)
    public float rotX;
    public float rotY;
    public float rotZ;

    public bool isMove;
}
