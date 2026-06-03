public class QuestUpdatePacket
{
    public PacketType type      = PacketType.QUEST_UPDATE;
    public string     nickname  = "";
    public string     questId   = "";   // 빈 문자열이면 퀘스트 없음 (초기화/완료 후)
    public string     questName = "";
    public int        stepIndex;
    public int        totalSteps;
    public bool       isComplete;
}
