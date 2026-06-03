using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퀘스트 인터랙션 대상 오브젝트. 씬에 배치 후 interactableId를 QuestStepData.targetId와 일치시킬 것.
/// </summary>
public class QuestInteractable : MonoBehaviour
{
    [Header("식별")]
    [Tooltip("QuestStepData.targetId와 일치해야 함")]
    public string interactableId;

    [Header("힌트 레이블")]
    [Tooltip("플레이어가 볼 때 표시할 아이템 이름 (비워두면 interactableId 사용)")]
    public string displayName;

    // 오버헤드 마커
    Canvas _markerCanvas;
    Text   _markerText;

    static readonly Color C_IDLE   = new Color(1f, 1f, 1f, 0.75f);
    static readonly Color C_FOCUS  = new Color(1f, 0.9f, 0.2f, 1f);

    void Start()
    {
        BuildMarker();
        SetMarkerVisible(false);
    }

    void Update()
    {
        if (_markerCanvas == null) return;

        // 퀘스트 활성화 중이고 현재 스텝의 targetId와 일치할 때만 표시
        var qm = QuestManager.Instance;
        bool relevant = qm != null && qm.ActiveQuest != null && !qm.IsQuestComplete
                     && qm.CurrentStep != null && qm.CurrentStep.targetId == interactableId;
        SetMarkerVisible(relevant);

        if (relevant)
        {
            bool focused = qm.FocusedInteractable == this;
            _markerText.color = focused ? C_FOCUS : C_IDLE;

            if (Camera.main != null)
            {
                Vector3 dir = Camera.main.transform.position - _markerCanvas.transform.position;
                _markerCanvas.transform.rotation = Quaternion.LookRotation(-dir);
            }
        }
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────────
    void BuildMarker()
    {
        string label = string.IsNullOrEmpty(displayName) ? interactableId : displayName;

        var go = new GameObject("InteractMarker");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        go.transform.localScale    = Vector3.one * 0.008f;

        _markerCanvas = go.AddComponent<Canvas>();
        _markerCanvas.renderMode   = RenderMode.WorldSpace;
        _markerCanvas.sortingOrder = 14;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 60f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);

        var tGO = new GameObject("Label");
        tGO.transform.SetParent(go.transform, false);
        var tRT = tGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(8f, 4f);
        tRT.offsetMax = new Vector2(-8f, -4f);
        _markerText = tGO.AddComponent<Text>();
        _markerText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _markerText.fontSize  = 22;
        _markerText.fontStyle = FontStyle.Bold;
        _markerText.alignment = TextAnchor.MiddleCenter;
        _markerText.color     = C_IDLE;
        _markerText.text      = label;
    }

    void SetMarkerVisible(bool v)
    {
        if (_markerCanvas != null) _markerCanvas.enabled = v;
    }
}
