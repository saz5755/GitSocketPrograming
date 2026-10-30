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
    public bool isFlying;
    public bool isBoardedInCockpit;
    public int  animState;

    // v4.4 — 송신자 시계(ms)·에어쇼 연출 상태 (바이너리 꼬리 필드, 구버전·JSON 은 0)
    public uint sendMs;
    public byte fx;
}
