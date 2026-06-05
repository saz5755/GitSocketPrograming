public class AISpawnPacket : Packet
{
    public string nickname = "";
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public int    aiType;   // 0 = Escort, 1 = Enemy
}
