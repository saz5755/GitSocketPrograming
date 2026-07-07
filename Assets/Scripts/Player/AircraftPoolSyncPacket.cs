using System.Collections.Generic;

public class AircraftEntry
{
    public string nickname           = "";
    public int    networkId          = -1;
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public float  speed;
    public bool   isFlying;
    public int    aiType             = -1;  // -1=플레이어기체, 0=에스코트봇, 1=에너미봇
    public string controllerNickname = "";
}

public class AircraftPoolSyncPacket : Packet
{
    public List<AircraftEntry> aircraft = new();
}
