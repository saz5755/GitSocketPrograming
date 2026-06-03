using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 퀘스트 진행 상태 관리 싱글턴.
/// GameScene의 빈 GameObject에 부착. F키 레이캐스트 인터랙션과 네트워크 동기화를 담당.
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }

    [SerializeField] float interactRange = 3.5f;

    // ── 로컬 퀘스트 상태 ────────────────────────────────────────────────────
    public QuestData ActiveQuest      { get; private set; }
    public int       CurrentStepIndex { get; private set; }
    public bool      IsQuestComplete  { get; private set; }
    public int       TaskCount        { get; private set; }

    // ── 원격 플레이어 퀘스트 상태 ───────────────────────────────────────────
    public readonly Dictionary<string, RemoteQuestState> RemoteStates = new();

    // ── 이벤트 ──────────────────────────────────────────────────────────────
    public event Action<QuestData>    OnQuestAccepted;
    public event Action<int>          OnStepAdvanced;    // 새 stepIndex
    public event Action<QuestData>    OnQuestCompleted;
    public event Action<string>       OnRemoteUpdated;   // nickname

    // 현재 프레임에서 레이캐스트로 감지 중인 대상 (UI 힌트용)
    public QuestNPC          FocusedNPC          { get; private set; }
    public QuestInteractable FocusedInteractable { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnQuestUpdate += HandleRemoteQuestUpdate;
            sc.OnSpawn       += OnRemoteSpawned;   // 새 플레이어 입장 → 내 상태 재전송
            sc.OnDespawn     += OnRemoteDespawned; // 퇴장 → RemoteStates 정리
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnQuestUpdate -= HandleRemoteQuestUpdate;
            sc.OnSpawn       -= OnRemoteSpawned;
            sc.OnDespawn     -= OnRemoteDespawned;
        }
    }

    void Update()
    {
        // 비행 중이거나 콕핏 탑승 중에는 퀘스트 인터랙션 비활성
        var gm = GameModeManager.Instance;
        if (gm != null && (gm.IsFlying || gm.IsBoardedInCockpit || gm.HasBoardingTarget)) return;

        UpdateFocus();
        CheckReachLocation();

        if (Input.GetKeyDown(KeyCode.F))
            TryInteract();
    }

    // ── 레이캐스트 포커스 ────────────────────────────────────────────────────
    void UpdateFocus()
    {
        FocusedNPC          = null;
        FocusedInteractable = null;

        var gc = GameModeManager.Instance?.GroundCharacter;
        if (gc == null) return;

        // 레이캐스트 대신 구 형태로 주변 오브젝트 감지 — 카메라 조준 불필요
        Collider[] cols = Physics.OverlapSphere(gc.position, interactRange);

        float bestNpc = float.MaxValue;
        float bestObj = float.MaxValue;

        foreach (var col in cols)
        {
            float dist = (col.transform.position - gc.position).sqrMagnitude;

            var npc = col.GetComponent<QuestNPC>();
            if (npc != null && dist < bestNpc) { bestNpc = dist; FocusedNPC = npc; }

            var obj = col.GetComponent<QuestInteractable>();
            if (obj != null && dist < bestObj) { bestObj = dist; FocusedInteractable = obj; }
        }
    }

    // ── F키 인터랙션 ────────────────────────────────────────────────────────
    void TryInteract()
    {
        if (FocusedNPC != null)          { HandleNPCInteract(FocusedNPC); return; }
        if (FocusedInteractable != null) { HandleInteractableInteract(FocusedInteractable); }
    }

    void HandleNPCInteract(QuestNPC npc)
    {
        if (ActiveQuest == null)
        {
            // 퀘스트 수락
            if (npc.questToOffer != null)
                AcceptQuest(npc.questToOffer);
            return;
        }

        if (IsQuestComplete)
        {
            // 보상 수령
            npc.TriggerReward();
            BroadcastReset();
            return;
        }

        var step = CurrentStep;
        if (step == null) return;

        if ((step.stepType == QuestStepType.TalkToNPC || step.stepType == QuestStepType.ReturnToNPC)
            && step.targetId == npc.npcId)
        {
            AdvanceStep();
        }
    }

    void HandleInteractableInteract(QuestInteractable obj)
    {
        if (ActiveQuest == null || IsQuestComplete) return;
        var step = CurrentStep;
        if (step == null || step.targetId != obj.interactableId) return;

        switch (step.stepType)
        {
            case QuestStepType.FindObject:
            case QuestStepType.InteractObject:
            case QuestStepType.SelectTool:
            case QuestStepType.EquipItem:
                AdvanceStep();
                break;

            case QuestStepType.UseTool:
            case QuestStepType.CompleteTask:
                TaskCount++;
                if (TaskCount >= step.requiredCount)
                    AdvanceStep();
                else
                    OnStepAdvanced?.Invoke(CurrentStepIndex); // 카운트 갱신 알림
                break;
        }
    }

    // ── ReachLocation 자동 감지 ──────────────────────────────────────────────
    void CheckReachLocation()
    {
        if (ActiveQuest == null || IsQuestComplete) return;
        var step = CurrentStep;
        if (step == null || step.stepType != QuestStepType.ReachLocation) return;

        var gc = GameModeManager.Instance?.GroundCharacter;
        if (gc == null) return;

        if ((gc.position - step.targetPosition).sqrMagnitude <= step.reachRadius * step.reachRadius)
            AdvanceStep();
    }

    // ── 퀘스트 상태 변경 ─────────────────────────────────────────────────────
    void AcceptQuest(QuestData quest)
    {
        ActiveQuest      = quest;
        CurrentStepIndex = 0;
        IsQuestComplete  = false;
        TaskCount        = 0;
        OnQuestAccepted?.Invoke(quest);
        SendQuestUpdate();
    }

    void AdvanceStep()
    {
        CurrentStepIndex++;
        TaskCount = 0;

        if (CurrentStepIndex >= ActiveQuest.steps.Length)
        {
            IsQuestComplete = true;
            OnQuestCompleted?.Invoke(ActiveQuest);
        }
        else
        {
            OnStepAdvanced?.Invoke(CurrentStepIndex);
        }
        SendQuestUpdate();
    }

    void BroadcastReset()
    {
        ActiveQuest      = null;
        CurrentStepIndex = 0;
        IsQuestComplete  = false;
        TaskCount        = 0;
        SendQuestUpdate();
    }

    public QuestStepData CurrentStep =>
        ActiveQuest != null && CurrentStepIndex < ActiveQuest.steps.Length
        ? ActiveQuest.steps[CurrentStepIndex] : null;

    // ── 네트워크 ─────────────────────────────────────────────────────────────
    void SendQuestUpdate()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.SendQuestUpdate(
            ActiveQuest?.questId   ?? "",
            ActiveQuest?.questName ?? "",
            CurrentStepIndex,
            ActiveQuest?.steps.Length ?? 0,
            IsQuestComplete);
    }

    void HandleRemoteQuestUpdate(QuestUpdatePacket p)
    {
        string myName = GameManager.Instance?.myNickname
                     ?? NetworkManager.Instance?.socketClient.myNickname ?? "";
        if (p.nickname == myName) return;

        if (string.IsNullOrEmpty(p.questId))
            RemoteStates.Remove(p.nickname);
        else
            RemoteStates[p.nickname] = new RemoteQuestState(p);

        OnRemoteUpdated?.Invoke(p.nickname);
    }

    // 새 플레이어가 룸에 입장하면 내 현재 퀘스트 상태를 재전송
    // → 입장자가 기존 진행 상황을 즉시 수신할 수 있음
    void OnRemoteSpawned(SpawnPacket _) => SendQuestUpdate();

    // 플레이어 퇴장 시 RemoteStates에서 제거하고 UI 갱신
    void OnRemoteDespawned(string nickname)
    {
        if (RemoteStates.Remove(nickname))
            OnRemoteUpdated?.Invoke(nickname);
    }
}

// ── 원격 플레이어 퀘스트 상태 DTO ───────────────────────────────────────────
public class RemoteQuestState
{
    public string nickname;
    public string questName;
    public int    stepIndex;
    public int    totalSteps;
    public bool   isComplete;

    public RemoteQuestState(QuestUpdatePacket p)
    {
        nickname   = p.nickname;
        questName  = p.questName;
        stepIndex  = p.stepIndex;
        totalSteps = p.totalSteps;
        isComplete = p.isComplete;
    }
}
