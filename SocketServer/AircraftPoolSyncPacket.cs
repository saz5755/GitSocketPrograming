using System.Collections.Generic;

class AircraftPoolSyncPacket : Packet
{
    public List<AircraftEntry> aircraft = new();
}
