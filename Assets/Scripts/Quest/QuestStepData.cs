using UnityEngine;

[CreateAssetMenu(menuName = "Quest/Step Data", fileName = "NewQuestStep")]
public class QuestStepData : ScriptableObject
{
    [Tooltip("단계 유형")]
    public QuestStepType stepType;

    [Tooltip("UI 체크리스트에 표시할 설명")]
    [TextArea] public string description;

    [Tooltip("대상 NPC의 npcId 또는 QuestInteractable의 interactableId와 일치해야 함")]
    public string targetId;

    [Tooltip("CompleteTask / UseTool 단계에서 완료에 필요한 횟수")]
    public int requiredCount = 1;

    [Tooltip("ReachLocation 단계에서의 목표 월드 위치")]
    public Vector3 targetPosition;

    [Tooltip("ReachLocation 단계에서의 도달 인정 반경")]
    public float reachRadius = 5f;
}
