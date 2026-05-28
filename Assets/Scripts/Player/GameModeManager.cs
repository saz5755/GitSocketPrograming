using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지상 모드 ↔ 비행 모드 전환을 총괄하는 싱글턴.
/// PlayerManager.CreatePlayer()에서 Init() 호출.
/// </summary>
public class GameModeManager : MonoBehaviour
{
    public static GameModeManager Instance { get; private set; }

    public bool IsFlying { get; private set; }

    // 외부(AircraftZone)에서 접근
    public Transform GroundCharacter => _gc != null ? _gc.transform : null;
    public Transform LocalAircraft   => _pc != null ? _pc.transform : null;

    GroundController _gc;
    PlayerController _pc;
    FlightCamera     _fc;
    FlightHUD        _hud;
    Renderer[]       _charRenderers;

    // 범위 내 존
    AircraftZone _activeZone;

    // 화면 하단 프롬프트 UI
    Canvas _promptCanvas;
    Text   _promptText;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── 초기화 ──────────────────────────────────────────────────────────────
    // groundPos: Zone과 동일한 지면 스냅 좌표. 생략 시 aircraftSpawnPos에서 계산.
    public void Init(GroundController gc, PlayerController pc,
                     Vector3 aircraftSpawnPos, Vector3 groundPos = default)
    {
        _gc = gc;
        _pc = pc;
        _fc  = FindObjectOfType<FlightCamera>();
        _hud = FindObjectOfType<FlightHUD>();
        _charRenderers = gc.GetComponentsInChildren<Renderer>(true);

        BuildPromptUI();

        // groundPos 미지정 시 spawnPos XZ에서 지형 스냅
        if (groundPos == default)
        {
            Vector3 origin = aircraftSpawnPos + Vector3.up * 500f;
            groundPos = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        ? hit.point
                        : new Vector3(aircraftSpawnPos.x, 0f, aircraftSpawnPos.z);
        }

        // 지상 캐릭터 시작 위치: Zone 12m 뒤, 지면 위 5cm
        Vector3 charStart = new Vector3(groundPos.x, groundPos.y + 0.05f, groundPos.z - 12f);
        ApplyGroundMode(charStart, 0f);
    }

    // ── 존 알림 ─────────────────────────────────────────────────────────────
    public void NotifyZoneEnter(AircraftZone zone) => _activeZone = zone;
    public void NotifyZoneExit(AircraftZone zone)
    {
        if (_activeZone == zone) _activeZone = null;
    }

    // ── 매 프레임: 프롬프트 표시 + F키 처리 ────────────────────────────────
    void Update()
    {
        if (_activeZone == null) { ShowPrompt(false); return; }

        bool canBoard   = !IsFlying && _activeZone.ZoneType == AircraftZone.Type.Takeoff;
        bool canExit    =  IsFlying && _activeZone.ZoneType == AircraftZone.Type.Landing;
        // Carrier: 비행 중 항모 갑판에 착함 (항모 이동 시 transform 기준 위치 전달)
        bool canCarrier =  IsFlying && _activeZone.ZoneType == AircraftZone.Type.Carrier;

        if (!canBoard && !canExit && !canCarrier) { ShowPrompt(false); return; }

        string prompt = canBoard   ? "[F]  BOARD AIRCRAFT"
                      : canCarrier ? "[F]  LAND ON CARRIER"
                      :              "[F]  EXIT AIRCRAFT";
        ShowPrompt(true, prompt);

        if (Input.GetKeyDown(KeyCode.F))
        {
            if (canBoard)        EnterFlight(_activeZone.transform.position);
            else if (canCarrier) ExitFlight(_activeZone.transform.position, _activeZone.transform);
            else                 ExitFlight(_activeZone.transform.position);
        }
    }

    // ── 비행 모드 진입 ───────────────────────────────────────────────────────
    public void EnterFlight(Vector3 boardingPos)
    {
        if (IsFlying || _gc == null || _pc == null) return;
        IsFlying = true;

        // 캐릭터 숨기기
        _gc.enabled = false;
        SetCharRenderers(false);

        // 항공기: 탑승 존 위치에 배치 후 활성화
        _pc.transform.SetPositionAndRotation(boardingPos, Quaternion.identity);
        _pc.enabled = true;

        // 카메라·HUD 전환
        _fc?.SetFlightTarget(_pc);
        _hud?.SetVisible(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        Debug.Log("[GameMode] → Flight Mode");
    }

    // ── 지상 모드 복귀 ───────────────────────────────────────────────────────
    // platform: 항모 등 이동 플랫폼의 Transform. 지정 시 캐릭터를 플랫폼 자식으로 배치.
    public void ExitFlight(Vector3 landingPos, Transform platform = null)
    {
        if (!IsFlying || _gc == null || _pc == null) return;
        IsFlying = false;

        float yaw = _pc.transform.eulerAngles.y;

        // 항공기 정지
        _pc.enabled = false;

        // 캐릭터를 착륙 위치에 배치
        Vector3 spawnPos = landingPos + new Vector3(0f, 0.05f, 0f);
        if (platform != null)
        {
            // 항모 갑판 위 착함: 캐릭터를 항모 좌표계 기준으로 부착
            _gc.transform.SetParent(platform, true);
        }
        _gc.transform.SetPositionAndRotation(spawnPos, Quaternion.Euler(0f, yaw, 0f));
        _gc.InitYaw(yaw);
        SetCharRenderers(true);
        _gc.enabled = true;

        // 카메라·HUD 전환
        _fc?.SetGroundTarget(_gc.transform);
        _hud?.SetVisible(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        Debug.Log(platform != null ? $"[GameMode] → Carrier Mode ({platform.name})" : "[GameMode] → Ground Mode");
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────
    void ApplyGroundMode(Vector3 charPos, float yaw)
    {
        IsFlying = false;

        _pc.enabled = false;

        _gc.transform.position = charPos;
        _gc.InitYaw(yaw);
        SetCharRenderers(true);
        _gc.enabled = true;

        _hud?.SetVisible(false);
        _fc?.SetGroundTarget(_gc.transform);
    }

    void SetCharRenderers(bool visible)
    {
        if (_charRenderers == null) return;
        foreach (var r in _charRenderers) r.enabled = visible;
    }

    // ── 프롬프트 UI ─────────────────────────────────────────────────────────
    void BuildPromptUI()
    {
        var go = new GameObject("ModePromptCanvas");

        _promptCanvas = go.AddComponent<Canvas>();
        _promptCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _promptCanvas.sortingOrder = 50;
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        var panel = new GameObject("Prompt");
        panel.transform.SetParent(go.transform, false);

        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 40f);
        rt.sizeDelta        = new Vector2(420f, 52f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.60f);

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(panel.transform, false);
        var tRT = txtGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;

        _promptText = txtGO.AddComponent<Text>();
        _promptText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _promptText.fontSize  = 22;
        _promptText.fontStyle = FontStyle.Bold;
        _promptText.color     = new Color(0f, 1f, 0.5f, 1f);
        _promptText.alignment = TextAnchor.MiddleCenter;

        _promptCanvas.enabled = false;
    }

    void ShowPrompt(bool show, string msg = "")
    {
        if (_promptCanvas == null) return;
        _promptCanvas.enabled = show;
        if (show && _promptText != null) _promptText.text = msg;
    }
}
