// Client → Server → Client (UDP): 미사일 실시간 위치 동기화 (10Hz)
public class MissileMovePacket : Packet
{
    public string missileId = "";
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public float  speed;
}
