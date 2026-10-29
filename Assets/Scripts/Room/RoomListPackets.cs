using System.Collections.Generic;

public class RoomInfo
{
    public int    roomId;
    public string roomName;
    public int    playerCount;
    public int    maxPlayers;
    public string mode;
}

[System.Serializable]
public class RoomSettingPacket : Packet
{
    public int enemySkill;           // 0=ROOKIE 1=VETERAN 2=ACE
}

public class RoomListResultPacket : Packet
{
    public List<RoomInfo> rooms = new();
}

public class LeaveRoomPacket : Packet { }

[System.Serializable]
public class CreateRoomPacket : Packet
{
    public string roomName;
    public int    maxPlayers = 8;
    public string mode;
}

[System.Serializable]
public class CreateRoomResultPacket : Packet
{
    public bool   success;
    public int    roomId;
    public string roomName;
    public string errorMessage;
}

[System.Serializable]
public class EnterRoomResultPacket : Packet
{
    public bool   success;
    public int    roomId;
    public string errorMessage;
    public string hostNickname;
    public string mode;
    public int    enemySkill = -1;   // 적 AI 숙련도 0~2 (구 서버면 -1 = 모름)
}
