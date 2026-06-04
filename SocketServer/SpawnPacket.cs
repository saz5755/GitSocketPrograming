class SpawnPacket : Packet
{
    public string nickname = string.Empty;

    // World Position
    public float x;
    public float y;
    public float z;

    // World Rotation (Euler)
    public float rotX;
    public float rotY;
    public float rotZ;

    public bool isMove;
    public bool isFlying;
    public bool isBoardedInCockpit;
}
