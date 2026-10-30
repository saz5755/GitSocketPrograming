// Server → Client (UDP): 다른 플레이어 위치/회전 수신
public class MoveBroadcastPacket : Packet
{
    public int tick;
    public string nickname;

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
    public int  animState;   // GroundAnimState: 0=Idle 1=Walk 2=Run 3=Jump

    // v4.4 — 송신자 시계(ms)·에어쇼 연출 상태. 구버전 패킷이면 0
    public uint sendMs;
    public byte fx;
    [Newtonsoft.Json.JsonIgnore] public double localTime = -1;   // 수신 스레드가 채우는 보간 타임라인 시각
}
