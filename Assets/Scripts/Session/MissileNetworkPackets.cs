[System.Serializable]
public class MissileSpawnPacket : Packet
{
    public string missileId;
    public string shooterNickname;
    public string targetNickname;
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public int    guidanceType;   // 0=HeatSeeking, 1=RadarGuided
}

[System.Serializable]
public class MissileDestroyPacket : Packet
{
    public string missileId;
    public string shooterNickname; // 발사자 (수신 시 자신이면 중복 이펙트 스킵)
    public string hitNickname;     // 피격자 (miss면 empty)
    public float  posX, posY, posZ;
}

[System.Serializable]
public class MissileWarnPacket : Packet
{
    public string shooterNickname;
    public string targetNickname;
    public int    lockLevel;      // 0=None 1=Searching 2=Locked 3=Fired
}

[System.Serializable]
public class MissileMovePacket : Packet
{
    public string missileId;
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public float  speed;
}

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

[System.Serializable]
public class MoveAckPacket : Packet
{
    public int   tick;
    public float posX, posY, posZ;
    public float rotX, rotY, rotZ;
}
