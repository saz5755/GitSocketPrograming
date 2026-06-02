using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 콕핏 탑승 중 스위치 호버 감지 및 클릭 처리.
/// 스위치는 FlightCamera 자식이므로 WorldToScreenPoint 결과가 커서 위치와 무관하게 고정됨.
/// GameModeManager.Init()에서 AddComponent로 생성됨.
/// </summary>
public class CockpitInteractionSystem : MonoBehaviour
{
    public static CockpitInteractionSystem Instance { get; private set; }

    const float HOVER_RADIUS = 62f;  // 스크린 픽셀 반경

    // 각 스위치가 클릭 가능한 프리플라이트 단계
    static readonly PreflightStage[] RequiredStage =
    {
        PreflightStage.Waiting,    // [0] APU
        PreflightStage.ApuReady,   // [1] ENG
        PreflightStage.EngineIdle, // [2] AV
    };
    static readonly string[] HoverLabels =
    {
        "[Click]  Start APU",
        "[Click]  Start Engine",
        "[Click]  Avionics ON",
    };

    // ── UI ──────────────────────────────────────────────────────────────────
    Canvas        _tooltipCanvas;
    RectTransform _tooltipRoot;
    Text          _tooltipText;

    bool _isActive;
    int  _hoveredIdx = -1;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        BuildTooltipUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetActive(bool active) { _isActive = active; }

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (!_isActive || _tooltipCanvas == null) return;

        var gm = GameModeManager.Instance;
        var pf = PreflightSystem.Instance;

        if (gm == null || pf == null || !gm.IsBoardedInCockpit)
        {
            _tooltipCanvas.enabled = false;
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) { _tooltipCanvas.enabled = false; return; }

        Vector2 mouse = Input.mousePosition;
        _hoveredIdx = -1;

        for (int i = 0; i < 3; i++)
        {
            if (pf.Stage != RequiredStage[i]) continue;

            var sw = CockpitSwitch.Get(i);
            if (sw == null) continue;

            Vector3 sp = cam.WorldToScreenPoint(sw.transform.position);
            if (sp.z < 0f) continue;

            if (Vector2.Distance(mouse, new Vector2(sp.x, sp.y)) < HOVER_RADIUS)
            {
                _hoveredIdx = i;
                break;
            }
        }

        if (_hoveredIdx >= 0)
        {
            var sw = CockpitSwitch.Get(_hoveredIdx);
            Vector3 sp = cam.WorldToScreenPoint(sw.transform.position);

            _tooltipRoot.anchoredPosition = new Vector2(sp.x, sp.y + 60f);
            _tooltipText.text             = HoverLabels[_hoveredIdx];
            _tooltipCanvas.enabled        = true;

            if (Input.GetMouseButtonDown(0))
                pf.OnSwitchClicked(_hoveredIdx);
        }
        else
        {
            _tooltipCanvas.enabled = false;
        }
    }

    // ── 툴팁 UI 빌드 ─────────────────────────────────────────────────────────
    void BuildTooltipUI()
    {
        var root = new GameObject("CockpitTooltipCanvas");
        _tooltipCanvas = root.AddComponent<Canvas>();
        _tooltipCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _tooltipCanvas.sortingOrder = 56;
        // ConstantPixelSize: WorldToScreenPoint 픽셀 = Canvas 픽셀
        root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // 컨테이너 — anchorMin/Max (0,0), 절대 픽셀 위치로 이동
        var container = new GameObject("Container");
        container.transform.SetParent(root.transform, false);
        _tooltipRoot            = container.AddComponent<RectTransform>();
        _tooltipRoot.anchorMin  = Vector2.zero;
        _tooltipRoot.anchorMax  = Vector2.zero;
        _tooltipRoot.pivot      = new Vector2(0.5f, 0f);
        _tooltipRoot.sizeDelta  = new Vector2(220f, 36f);

        // 배경
        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(container.transform, false);
        var bgRT       = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        // 텍스트
        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(container.transform, false);
        var txtRT      = txtGO.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = txtRT.offsetMax = Vector2.zero;

        _tooltipText           = txtGO.AddComponent<Text>();
        _tooltipText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _tooltipText.fontSize  = 15;
        _tooltipText.fontStyle = FontStyle.Bold;
        _tooltipText.color     = new Color(1f, 0.88f, 0.25f, 1f);
        _tooltipText.alignment = TextAnchor.MiddleCenter;

        _tooltipCanvas.enabled = false;
    }
}
