using UnityEngine;

[CreateAssetMenu(menuName = "Quest/Quest Data", fileName = "NewQuest")]
public class QuestData : ScriptableObject
{
    [Tooltip("퀘스트 고유 ID (네트워크 동기화에 사용)")]
    public string questId;

    [Tooltip("UI에 표시할 퀘스트 이름")]
    public string questName;

    [TextArea] public string description;

    [Tooltip("순서대로 진행할 단계 목록")]
    public QuestStepData[] steps;

    [Tooltip("완료 시 NPC가 지급하는 보상 설명")]
    [TextArea] public string rewardDescription;
}
