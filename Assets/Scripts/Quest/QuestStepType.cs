public enum QuestStepType
{
    TalkToNPC,       // NPC에게 말걸기 (퀘스트 수락 첫 대화)
    ReachLocation,   // 특정 위치 도달
    FindObject,      // 특정 오브젝트 범위 내 접근
    InteractObject,  // F키로 오브젝트 상호작용
    SelectTool,      // F키로 도구 선택
    EquipItem,       // F키로 아이템 획득/장착
    UseTool,         // F키로 도구 사용 (횟수 카운트)
    CompleteTask,    // F키 N회 수행
    ReturnToNPC      // NPC에게 보고 (퀘스트 완료)
}
