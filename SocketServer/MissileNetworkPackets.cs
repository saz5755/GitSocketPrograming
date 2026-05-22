public class MissileSpawnPacket : Packet
{
    public string missileId       = "";
    public string shooterNickname = "";
    public string targetNickname  = "";
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public int    guidanceType;
}

public class MissileDestroyPacket : Packet
{
    public string missileId       = "";
    public string shooterNickname = "";   // 발사자 (클라 중복 방지용)
    public string hitNickname     = "";   // 피격자 (miss면 empty)
    public float  posX, posY, posZ;
}

public class MissileWarnPacket : Packet
{
    public string shooterNickname = "";
    public string targetNickname  = "";
    public int    lockLevel;
}
