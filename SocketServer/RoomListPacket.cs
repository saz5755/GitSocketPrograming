using System.Collections.Generic;

public class RoomInfo
{
    public int roomId;
    public string roomName = string.Empty;
    public int playerCount;
    public int maxPlayers;
}

public class RoomListRequestPacket : Packet { }

public class RoomListResultPacket : Packet
{
    public List<RoomInfo> rooms = new();
}
