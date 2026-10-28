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
}
