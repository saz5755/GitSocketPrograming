using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퀘스트를 제공하거나 완료 보고를 받는 NPC.
/// 씬에 배치된 오브젝트에 부착. QuestInteractable은 필요 없음 (NPC 전용 처리).
/// </summary>
public class QuestNPC : MonoBehaviour
{
    [Header("식별")]
    [Tooltip("QuestStepData.targetId와 일치해야 함")]
    public string npcId;

    [Header("퀘스트")]
    public QuestData questToOffer;

    [Header("대사")]
    [TextArea] public string dialogueIdle      = "안녕하세요. 도움이 필요하신가요?";
    [TextArea] public string dialogueAccept     = "좋아요! 임무를 시작해봅시다.";
    [TextArea] public string dialogueInProgress = "아직 진행 중이군요. 파이팅!";
    [TextArea] public string dialogueComplete   = "훌륭합니다! 보상을 드리겠습니다.";

    // 오버헤드 마커 (!  /  ?)
    Canvas _markerCanvas;
    Text   _markerText;

    // 말풍선 패널
    Canvas _speechCanvas;
    Text   _speechText;
    float  _speechTimer;
    const float SPEECH_DURATION = 3f;

    static readonly Color C_AVAIL    = new Color(1.00f, 0.85f, 0.00f, 1f); // 노랑 !
    static readonly Color C_PROGRESS = new Color(0.40f, 0.80f, 1.00f, 1f); // 파랑 ?
    static readonly Color C_DONE     = new Color(0.20f, 1.00f, 0.40f, 1f); // 초록 ✔

    void Start()
    {
        BuildMarker();
        BuildSpeechBubble();
        SetSpeechVisible(false);
    }

    void Update()
    {
        UpdateMarker();

        if (_speechTimer > 0f)
        {
            _speechTimer -= Time.deltaTime;
            if (_speechTimer <= 0f) SetSpeechVisible(false);
        }

        // 말풍선이 보이면 카메라를 향하게
        if (_speechCanvas != null && _speechCanvas.enabled && Camera.main != null)
        {
            Vector3 dir = Camera.main.transform.position - _speechCanvas.transform.position;
            _speechCanvas.transform.rotation = Quaternion.LookRotation(-dir);
        }
    }

    // ── QuestManager에서 호출 ────────────────────────────────────────────────
    public void TriggerReward()
    {
        ShowSpeech(dialogueComplete);
        Debug.Log($"[QuestNPC:{npcId}] 퀘스트 완료 보상 지급: {questToOffer?.rewardDescription}");
    }

    public void ShowAcceptDialogue()   => ShowSpeech(dialogueAccept);
    public void ShowProgressDialogue() => ShowSpeech(dialogueInProgress);

    // ── 오버헤드 마커 상태 ───────────────────────────────────────────────────
    void UpdateMarker()
    {
        if (_markerText == null) return;
        var qm = QuestManager.Instance;

        if (qm == null || qm.ActiveQuest == null)
        {
            // 제공할 퀘스트가 있으면 !
            bool hasQuest = questToOffer != null;
            _markerText.text  = hasQuest ? "!" : "";
            _markerText.color = C_AVAIL;
        }
        else if (qm.IsQuestComplete)
        {
            // 보상 수령 대기
            _markerText.text  = "✔";
            _markerText.color = C_DONE;
        }
        else
        {
            // 진행 중
            _markerText.text  = "?";
            _markerText.color = C_PROGRESS;
        }

        // 마커를 항상 카메라 방향으로
        if (Camera.main != null)
        {
            Vector3 dir = Camera.main.transform.position - _markerCanvas.transform.position;
            _markerCanvas.transform.rotation = Quaternion.LookRotation(-dir);
        }
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────────
    void BuildMarker()
    {
        var go = new GameObject("NPCMarker");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 2.4f, 0f);
        go.transform.localScale    = Vector3.one * 0.012f;

        _markerCanvas = go.AddComponent<Canvas>();
        _markerCanvas.renderMode   = RenderMode.WorldSpace;
        _markerCanvas.sortingOrder = 15;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);

        // 배경 원
        var bgGO  = new GameObject("BG");
        bgGO.transform.SetParent(go.transform, false);
        var bgRT  = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = bgRT.anchorMax = new Vector2(0.5f, 0.5f);
        bgRT.sizeDelta = new Vector2(70f, 70f);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);

        // 마커 텍스트
        var tGO = new GameObject("Marker");
        tGO.transform.SetParent(go.transform, false);
        var tRT = tGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
        _markerText = tGO.AddComponent<Text>();
        _markerText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _markerText.fontSize  = 56;
        _markerText.fontStyle = FontStyle.Bold;
        _markerText.alignment = TextAnchor.MiddleCenter;
        _markerText.text      = "!";
        _markerText.color     = C_AVAIL;
    }

    void BuildSpeechBubble()
    {
        var go = new GameObject("SpeechBubble");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 3.2f, 0f);
        go.transform.localScale    = Vector3.one * 0.008f;

        _speechCanvas = go.AddComponent<Canvas>();
        _speechCanvas.renderMode   = RenderMode.WorldSpace;
        _speechCanvas.sortingOrder = 16;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(360f, 80f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.10f, 0.88f);

        var tGO = new GameObject("Text");
        tGO.transform.SetParent(go.transform, false);
        var tRT = tGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(10f, 6f);
        tRT.offsetMax = new Vector2(-10f, -6f);
        _speechText = tGO.AddComponent<Text>();
        _speechText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _speechText.fontSize  = 20;
        _speechText.color     = Color.white;
        _speechText.alignment = TextAnchor.MiddleCenter;
    }

    void ShowSpeech(string msg)
    {
        if (_speechText == null) return;
        _speechText.text = msg;
        SetSpeechVisible(true);
        _speechTimer = SPEECH_DURATION;
    }

    void SetSpeechVisible(bool v)
    {
        if (_speechCanvas != null) _speechCanvas.enabled = v;
    }
}
