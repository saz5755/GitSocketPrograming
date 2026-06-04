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
    public int hp = 100;

    public DateTime lastInputTime;
}
