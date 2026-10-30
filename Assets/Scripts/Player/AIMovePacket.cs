public class AIMovePacket : Packet
{
    public string nickname = "";
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public float  speed;
    public bool   isMove;

    // v4.4 배치 — 송신 시각(ms)·에어쇼 연출 상태
    public uint   sendMs;
    public byte   fx;
    [Newtonsoft.Json.JsonIgnore] public double localTime = -1;
}
