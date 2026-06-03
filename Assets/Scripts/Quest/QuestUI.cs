using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퀘스트 HUD. Managers GO에 부착하거나 별도 Canvas GO에 부착.
/// QuestManager 이벤트를 구독하여 자동 갱신.
/// </summary>
public class QuestUI : MonoBehaviour
{
    // ── 로컬 퀘스트 패널 ─────────────────────────────────────────────────────
    Canvas     _mainCanvas;
    GameObject _localPanel;
    Text       _questTitle;
    GameObject _stepsContainer;
    List<Text> _stepTexts = new();

    // ── 원격 플레이어 패널 ───────────────────────────────────────────────────
    GameObject _remotePanel;
    Text       _remoteTitle;
    List<Text> _remoteEntries = new();

    // ── 인터랙션 힌트 ────────────────────────────────────────────────────────
    GameObject _hintPanel;
    Text       _hintText;

    // 패널 너비/높이 상수
    const float PANEL_W     = 320f;
    const float STEP_H      = 28f;
    const float HEADER_H    = 36f;
    const float PADDING     = 10f;
    const float REMOTE_W    = 260f;
    const float REMOTE_ROW  = 24f;

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        BuildCanvas();
        BuildLocalPanel();
        BuildRemotePanel();
        BuildHintPanel();

        SetLocalVisible(false);
        SetRemoteVisible(false);
        SetHintVisible(false);

        var qm = QuestManager.Instance;
        if (qm != null)
        {
            qm.OnQuestAccepted  += OnQuestAccepted;
            qm.OnStepAdvanced   += OnStepAdvanced;
            qm.OnQuestCompleted += OnQuestCompleted;
            qm.OnRemoteUpdated  += OnRemoteUpdated;
        }
    }

    void OnDestroy()
    {
        var qm = QuestManager.Instance;
        if (qm != null)
        {
            qm.OnQuestAccepted  -= OnQuestAccepted;
            qm.OnStepAdvanced   -= OnStepAdvanced;
            qm.OnQuestCompleted -= OnQuestCompleted;
            qm.OnRemoteUpdated  -= OnRemoteUpdated;
        }
    }

    // ── 이벤트 핸들러 ────────────────────────────────────────────────────────
    void OnQuestAccepted(QuestData q)
    {
        RefreshLocal();
        SetLocalVisible(true);
    }

    void OnStepAdvanced(int stepIndex) => RefreshLocal();

    void OnQuestCompleted(QuestData q)
    {
        RefreshLocal();
    }

    void OnRemoteUpdated(string nickname) => RefreshRemote();

    // ── 매 프레임: 인터랙션 힌트 ────────────────────────────────────────────
    void Update()
    {
        UpdateHint();
    }

    // ── 로컬 퀘스트 갱신 ────────────────────────────────────────────────────
    void RefreshLocal()
    {
        var qm = QuestManager.Instance;
        if (qm == null || qm.ActiveQuest == null) { SetLocalVisible(false); return; }

        var quest = qm.ActiveQuest;
        _questTitle.text = qm.IsQuestComplete
            ? $"<color=#33FF66>✔ {quest.questName}</color>"
            : quest.questName;

        // 스텝 텍스트 수 맞추기
        while (_stepTexts.Count < quest.steps.Length)
            _stepTexts.Add(AddStepText());

        for (int i = 0; i < quest.steps.Length; i++)
        {
            var step = quest.steps[i];
            bool done   = i < qm.CurrentStepIndex || qm.IsQuestComplete;
            bool active = i == qm.CurrentStepIndex && !qm.IsQuestComplete;

            string prefix = done ? "<color=#33FF66>■</color>" : "□";
            string col    = done ? "#33FF66" : (active ? "#FFFFFF" : "#888888");
            string desc   = step.description;

            if (active && step.requiredCount > 1)
                desc += $"  ({qm.TaskCount}/{step.requiredCount})";

            _stepTexts[i].text    = $"{prefix} <color={col}>{desc}</color>";
            _stepTexts[i].gameObject.SetActive(true);
        }
        // 남은 슬롯 숨기기
        for (int i = quest.steps.Length; i < _stepTexts.Count; i++)
            _stepTexts[i].gameObject.SetActive(false);

        // 패널 높이 재조정
        float h = HEADER_H + PADDING + quest.steps.Length * STEP_H + PADDING;
        var rt = _localPanel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(PANEL_W, h);

        SetLocalVisible(true);
    }

    // ── 원격 플레이어 갱신 ──────────────────────────────────────────────────
    void RefreshRemote()
    {
        var qm = QuestManager.Instance;
        if (qm == null || qm.RemoteStates.Count == 0) { SetRemoteVisible(false); return; }

        while (_remoteEntries.Count < qm.RemoteStates.Count)
            _remoteEntries.Add(AddRemoteEntry());

        int idx = 0;
        foreach (var kv in qm.RemoteStates)
        {
            var rs = kv.Value;
            string progress = rs.isComplete
                ? "<color=#33FF66>완료</color>"
                : $"{rs.stepIndex}/{rs.totalSteps}";
            _remoteEntries[idx].text = $"<color=#AADDFF>{rs.nickname}</color>  {rs.questName}  {progress}";
            _remoteEntries[idx].gameObject.SetActive(true);
            idx++;
        }
        for (int i = idx; i < _remoteEntries.Count; i++)
            _remoteEntries[i].gameObject.SetActive(false);

        float h = HEADER_H + PADDING + idx * REMOTE_ROW + PADDING;
        var rt = _remotePanel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(REMOTE_W, h);

        SetRemoteVisible(true);
    }

    // ── 인터랙션 힌트 갱신 ──────────────────────────────────────────────────
    void UpdateHint()
    {
        var qm = QuestManager.Instance;
        if (qm == null) { SetHintVisible(false); return; }

        if (qm.FocusedNPC != null)
        {
            string hint = (qm.ActiveQuest == null)
                ? $"[F]  {qm.FocusedNPC.npcId}  ▶  퀘스트 수락"
                : qm.IsQuestComplete
                    ? $"[F]  {qm.FocusedNPC.npcId}  ▶  보상 수령"
                    : $"[F]  {qm.FocusedNPC.npcId}  ▶  대화";
            _hintText.text = hint;
            SetHintVisible(true);
            return;
        }

        if (qm.FocusedInteractable != null && qm.ActiveQuest != null && !qm.IsQuestComplete)
        {
            var step = qm.CurrentStep;
            if (step != null && step.targetId == qm.FocusedInteractable.interactableId)
            {
                string name = string.IsNullOrEmpty(qm.FocusedInteractable.displayName)
                    ? qm.FocusedInteractable.interactableId
                    : qm.FocusedInteractable.displayName;
                _hintText.text = $"[F]  {name}  ▶  {step.description}";
                SetHintVisible(true);
                return;
            }
        }

        SetHintVisible(false);
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────────
    void BuildCanvas()
    {
        var go = new GameObject("QuestCanvas");
        go.transform.SetParent(transform, false);
        _mainCanvas = go.AddComponent<Canvas>();
        _mainCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _mainCanvas.sortingOrder = 20;
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        go.AddComponent<GraphicRaycaster>();
    }

    void BuildLocalPanel()
    {
        _localPanel = MakePanel(new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(PANEL_W, 200f));

        // 제목
        var titleGO = MakeText(_localPanel.transform, "Title",
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(PADDING, -(HEADER_H)), new Vector2(-PADDING, 0f),
            18, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.2f));
        _questTitle = titleGO;

        // 구분선
        var line = new GameObject("Sep");
        line.transform.SetParent(_localPanel.transform, false);
        var lineRT = line.AddComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0f, 1f); lineRT.anchorMax = new Vector2(1f, 1f);
        lineRT.sizeDelta = new Vector2(0f, 1f);
        lineRT.anchoredPosition = new Vector2(0f, -HEADER_H);
        line.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

        // 스텝 컨테이너
        _stepsContainer = new GameObject("Steps");
        _stepsContainer.transform.SetParent(_localPanel.transform, false);
        var scRT = _stepsContainer.AddComponent<RectTransform>();
        scRT.anchorMin = Vector2.zero; scRT.anchorMax = Vector2.one;
        scRT.offsetMin = new Vector2(PADDING, PADDING);
        scRT.offsetMax = new Vector2(-PADDING, -(HEADER_H + 2f));
    }

    void BuildRemotePanel()
    {
        _remotePanel = MakePanel(new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(REMOTE_W, 100f));

        var titleGO = MakeText(_remotePanel.transform, "RemTitle",
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(PADDING, -HEADER_H), new Vector2(-PADDING, 0f),
            14, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.6f, 0.85f, 1f));
        titleGO.text = "다른 플레이어 퀘스트";
        _remoteTitle = titleGO;
    }

    void BuildHintPanel()
    {
        // 화면 하단 중앙
        var go = new GameObject("HintPanel");
        go.transform.SetParent(_mainCanvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(500f, 40f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);

        var tGO = new GameObject("HintText");
        tGO.transform.SetParent(go.transform, false);
        var tRT = tGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(12f, 4f);
        tRT.offsetMax = new Vector2(-12f, -4f);
        _hintText = tGO.AddComponent<Text>();
        _hintText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _hintText.fontSize  = 18;
        _hintText.color     = Color.white;
        _hintText.alignment = TextAnchor.MiddleCenter;
        _hintText.supportRichText = true;

        _hintPanel = go;
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────────────
    Text AddStepText()
    {
        int idx = _stepTexts.Count;
        var go  = new GameObject($"Step{idx}");
        go.transform.SetParent(_stepsContainer.transform, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, -idx * STEP_H);
        rt.sizeDelta = new Vector2(0f, STEP_H);

        var t = go.AddComponent<Text>();
        t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize      = 15;
        t.color         = Color.white;
        t.alignment     = TextAnchor.MiddleLeft;
        t.supportRichText = true;
        return t;
    }

    Text AddRemoteEntry()
    {
        int idx = _remoteEntries.Count;
        var go  = new GameObject($"Remote{idx}");
        go.transform.SetParent(_remotePanel.transform, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(PADDING, -(HEADER_H + PADDING + idx * REMOTE_ROW));
        rt.sizeDelta = new Vector2(-PADDING * 2f, REMOTE_ROW);

        var t = go.AddComponent<Text>();
        t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize      = 13;
        t.color         = Color.white;
        t.alignment     = TextAnchor.MiddleLeft;
        t.supportRichText = true;
        return t;
    }

    GameObject MakePanel(Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(_mainCanvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor; rt.anchorMax = anchor;
        rt.pivot     = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
        go.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.08f, 0.82f);
        return go;
    }

    Text MakeText(Transform parent, string name,
                  Vector2 anchorMin, Vector2 anchorMax,
                  Vector2 offsetMin, Vector2 offsetMax,
                  int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var t   = go.AddComponent<Text>();
        t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize      = fontSize;
        t.fontStyle     = style;
        t.alignment     = alignment;
        t.color         = color;
        t.supportRichText = true;
        return t;
    }

    void SetLocalVisible(bool v)  { if (_localPanel  != null) _localPanel.SetActive(v); }
    void SetRemoteVisible(bool v) { if (_remotePanel != null) _remotePanel.SetActive(v); }
    void SetHintVisible(bool v)   { if (_hintPanel   != null) _hintPanel.SetActive(v); }
}
