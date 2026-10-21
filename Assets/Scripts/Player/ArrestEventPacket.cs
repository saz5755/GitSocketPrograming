// 게스트 플레이어의 어레스팅 와이어 이벤트를 호스트에게 전달하는 패킷 (TCP).
// eventType: 0 = 와이어 체결, 1 = 하차(에스코트 착함 시작)
public class ArrestEventPacket : Packet
{
    public string nickname;
    public int    eventType;
    public float  approachDirX, approachDirY, approachDirZ;
    public float  wirePosX, wirePosY, wirePosZ;
}
