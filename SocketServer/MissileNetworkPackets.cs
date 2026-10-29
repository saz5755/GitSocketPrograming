public class MissileSpawnPacket : Packet
{
    public string missileId       = "";
    public string shooterNickname = "";
    public string targetNickname  = "";
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public int    guidanceType;
    public string weapon          = "";
}

// COUNTERMEASURE (TCP): 플레어/채프 1발 — 방 전체에 중계해 각 클라이언트가 시뮬레이션하는 미사일이 판정한다
public class CountermeasurePacket : Packet
{
    public string ownerNickname = "";
    public int    cmType;
    public float  posX, posY, posZ;
    public float  velX, velY, velZ;
}

public class MissileDestroyPacket : Packet
{
    public string missileId       = "";
    public string shooterNickname = "";   // 발사자 (클라 중복 방지용)
    public string hitNickname     = "";   // 피격자 (miss면 empty)
    public float  damage;                 // 발사 측이 계산한 피해 (근접신관 거리 감쇠 반영)
    public float  posX, posY, posZ;
}

public class MissileWarnPacket : Packet
{
    public string shooterNickname = "";
    public string targetNickname  = "";
    public int    lockLevel;
}
