public class CreateRoomPacket : Packet
{
    public string? roomName;
    public int     maxPlayers = 8;
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
}
