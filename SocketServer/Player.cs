public class Player
{
    public int playerId;
    public string nickname = string.Empty;
    public int lastProcessedTick;

    public Room? room;
    public ClientSession session = null!;

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
    public uint sendMs;   // v4.4 마지막 MOVE 의 송신자 시계 (게스트 보간 타임라인)
    public byte fx;       // v4.4 에어쇼 연출 상태 (스모크)
    public int  boardedAircraftId = -1;
    public int hp = 100;

    public DateTime lastInputTime;
}
