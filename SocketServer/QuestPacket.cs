public class QuestUpdatePacket
{
    public PacketType type      = PacketType.QUEST_UPDATE;
    public string     nickname  = "";
    public string     questId   = "";
    public string     questName = "";
    public int        stepIndex;
    public int        totalSteps;
    public bool       isComplete;
}
