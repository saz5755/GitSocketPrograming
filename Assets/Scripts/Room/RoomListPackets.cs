using System.Collections.Generic;

public class RoomInfo
{
    public int    roomId;
    public string roomName;
    public int    playerCount;
    public int    maxPlayers;
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
}
