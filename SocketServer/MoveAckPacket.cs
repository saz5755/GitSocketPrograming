// Server → Client (UDP): 서버가 처리 완료된 이동 틱과 저장 위치 에코
public class MoveAckPacket : Packet
{
    public int   tick;
    public float posX, posY, posZ;
    public float rotX, rotY, rotZ;
}
