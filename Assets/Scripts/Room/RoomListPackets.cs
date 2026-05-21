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
