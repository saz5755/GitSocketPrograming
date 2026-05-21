public class Player
{
    public int playerId;
    public string nickname;
    public int lastProcessedTick;

    public Room room;
    public ClientSession session;

    // World Position
    public float posX;
    public float posY;
    public float posZ;

    // World Rotation (Euler)
    public float rotX;
    public float rotY;
    public float rotZ;

    public bool isMove;
    public int hp = 100;

    public DateTime lastInputTime;
}
