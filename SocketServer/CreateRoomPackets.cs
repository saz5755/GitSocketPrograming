public class CreateRoomPacket : Packet
{
    public string? roomName;
    public int     maxPlayers = 8;
    public string? mode;            // RoomModes.Airshow / Combat (없으면 Combat)
}

public class CreateRoomResultPacket : Packet
{
    public bool    success;
    public int     roomId;
    public string? roomName;
    public string? errorMessage;
}

public class EnterRoomResultPacket : Packet
{
    public bool    success;
    public int     roomId;
    public string? errorMessage;
    public string? hostNickname;
    public string? mode;
    public int     enemySkill = 1;   // 0=ROOKIE 1=VETERAN 2=ACE
}

// ROOM_SETTING (TCP): 방장만 변경 가능한 방 설정 — 서버가 보관하고 방 전체(발신자 포함)에 중계
public class RoomSettingPacket : Packet
{
    public int enemySkill = 1;
}
