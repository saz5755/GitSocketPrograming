[System.Serializable]
public class GunFirePacket : Packet
{
    public string shooterNickname;
    public float  posX, posY, posZ;   // muzzle world position
    public float  dirX, dirY, dirZ;   // normalized fire direction
}

[System.Serializable]
public class GunHitPacket : Packet
{
    public string shooterNickname;
    public string targetNickname;
    public float  posX, posY, posZ;   // hit world position
}
