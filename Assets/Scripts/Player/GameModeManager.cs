using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지상 모드 ↔ 비행 모드 전환을 총괄하는 싱글턴.
/// PlayerManager.CreatePlayer()에서 Init() 호출.
/// </summary>
public class GameModeManager : MonoBehaviour
{
    public static GameModeManager Instance { get; private set; }

    public bool IsFlying          { get; private set; }
    public bool IsBoardedInCockpit { get; private set; }

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

    // 항모 카타펄트: 이함 직전 항모 참조를 임시 저장
    CarrierController _boardingCarrier;

    // 근접 탑승 트리거 — AircraftBoardingTrigger가 설정
    AircraftBoardingTrigger _boardingTrigger;

    // 항모 착함 자동 감속 (어레스팅 와이어)
    bool      _trapping;
    Coroutine _trapRoutine;


    // 화면 하단 프롬프트 UI
    Canvas    _promptCanvas;
    Text      _promptText;
    Image     _keyBoxImage;
    Text      _keyBoxText;

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
        _fc  = FindFirstObjectByType<FlightCamera>();
        _hud = FindFirstObjectByType<FlightHUD>();
        _charRenderers = gc.GetComponentsInChildren<Renderer>(true);

        // 프리플라이트 시스템 초기화
        if (GetComponent<PreflightSystem>() == null)
        {
            var pf = gameObject.AddComponent<PreflightSystem>();
            pf.Initialize(LaunchOfficerNPC.Instance);
        }

        // 콕핏 인터랙션 시스템 초기화
        if (GetComponent<CockpitInteractionSystem>() == null)
            gameObject.AddComponent<CockpitInteractionSystem>();

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
    public void NotifyZoneEnter(AircraftZone zone)
    {
        _activeZone = zone;

        // 항모 착함 존 진입 시 속도 조건 충족하면 자동 TRAP 시작
        if (IsFlying && zone.ZoneType == AircraftZone.Type.Carrier && !_trapping && _pc != null)
        {
            float kph = _pc.CurrentSpeed * 3.6f;
            if (kph <= 360f)
                _trapRoutine = StartCoroutine(TrapCoroutine(zone.transform));
        }
    }

    public void NotifyZoneExit(AircraftZone zone)
    {
        if (_activeZone == zone)
            _activeZone = null;
        // 트래핑 중이면 코루틴 유지
    }

    // 새 플레이어가 입장했을 때 현재 상태를 재전송 (타이밍 경쟁 보완)
    public void BroadcastCurrentState()
    {
        if (IsBoardedInCockpit)
            _pc?.SendCockpitState(true);
        else if (IsFlying)
            _pc?.SendFlightState(true);
    }

    // ── 근접 탑승 트리거 API (AircraftBoardingTrigger에서 호출) ─────────────
    public bool HasBoardingTarget => _boardingTrigger != null;
    public void SetBoardingTarget(AircraftBoardingTrigger trigger)   => _boardingTrigger = trigger;
    public void ClearBoardingTarget(AircraftBoardingTrigger trigger) { if (_boardingTrigger == trigger) _boardingTrigger = null; }

    // ── 매 프레임: 프롬프트 표시 + E키 처리 ────────────────────────────────
    void Update()
    {
        // ── 비행 중: 착함·착륙 존 처리 ──────────────────────────────────────
        if (IsFlying)
        {
            if (_activeZone == null) { ShowPrompt(false); return; }

            if (_activeZone.ZoneType == AircraftZone.Type.Carrier)
            {
                if (_trapping)
                {
                    ShowPrompt(true, "MANUAL TRAP — SKIP DECEL", new Color(1f, 0.75f, 0f, 1f), keyLabel: "F");
                    if (Input.GetKeyDown(KeyCode.F))
                    {
                        StopTrap();
                        ExitFlight(_activeZone.transform.position, _activeZone.transform);
                    }
                    return;
                }
                float kph     = _pc.CurrentSpeed * 3.6f;
                bool  waveOff = kph > 360f;
                ShowPrompt(true,
                    waveOff ? $"WAVE OFF  {kph:F0} KPH — REDUCE SPEED" : $"AUTO TRAP  {kph:F0} KPH",
                    waveOff ? new Color(1f, 0.2f, 0.1f, 1f) : new Color(0f, 1f, 0.5f, 1f),
                    keyLabel: waveOff ? "" : "F");
                if (!waveOff && Input.GetKeyDown(KeyCode.F))
                    ExitFlight(_activeZone.transform.position, _activeZone.transform);
                return;
            }

            if (_activeZone.ZoneType == AircraftZone.Type.Landing)
            {
                ShowPrompt(true, "EXIT AIRCRAFT", keyLabel: "F");
                if (Input.GetKeyDown(KeyCode.F))
                    ExitFlight(_activeZone.transform.position);
                return;
            }

            ShowPrompt(false);
            return;
        }

        // ── 지상: 콕핏 탑승 중 (프리플라이트 → 출격) ────────────────────────
        if (IsBoardedInCockpit)
        {
            var pf = PreflightSystem.Instance;
            if (pf != null && pf.IsReadyForLaunch)
            {
                _boardingCarrier ??= FindFirstObjectByType<CarrierController>();
                string lbl = _boardingCarrier != null ? "CATAPULT LAUNCH" : "TAKE OFF";
                ShowPrompt(true, lbl, new Color(0f, 1f, 0.5f, 1f), keyLabel: "F");
                if (Input.GetKeyDown(KeyCode.F))
                {
                    IsBoardedInCockpit = false;
                    _fc?.EndCockpitBoarding();
                    pf.ResetPreflight();
                    CockpitInteractionSystem.Instance?.SetActive(false);
                    EnterFlight(_pc.transform.position);
                }
            }
            else
            {
                ShowPrompt(true, "[ESC]  EXIT COCKPIT", new Color(1f, 0.72f, 0.20f, 1f), keyLabel: "");
            }
            if (Input.GetKeyDown(KeyCode.Escape)) ExitCockpit();
            return;
        }

        // ── 지상: 근접 항공기에 E키 탑승 ────────────────────────────────────
        // (원형 E키 UI는 AircraftBoardingTrigger 월드 UI가 표시)
        if (_boardingTrigger != null && Input.GetKeyDown(KeyCode.F))
        {
            var boardedPC = _boardingTrigger.Aircraft;

            // 에스코트 AI 기체 탑승 시: 해당 AI를 플레이어 기체로, 기존 기체를 에스코트 AI로 전환
            if (boardedPC.GetComponent<AIBotController>() != null)
                AIManager.Instance?.TakeOverEscortBot(boardedPC, _pc);

            _pc              = boardedPC;
            _boardingCarrier = FindFirstObjectByType<CarrierController>();
            EnterCockpit(_pc.transform.position);
        }

        ShowPrompt(false);
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
        // 항모 카타펄트: 항모 방향으로 정렬 후 초기 속도 부여. 없으면 사전 배치 회전 유지.
        Quaternion launchRot = _boardingCarrier != null
            ? _boardingCarrier.transform.rotation
            : _pc.transform.rotation;
        _pc.transform.SetPositionAndRotation(boardingPos, launchRot);
        _pc.enabled = true;

        // 캐리어 유무와 관계없이 프리플라이트 완료 후 발진이면 항상 카타펄트
        float catapultSpd = _boardingCarrier != null ? _boardingCarrier.CatapultSpeed : 72f;
        _boardingCarrier = null;
        _pc.StartCatapult(catapultSpd);
        _pc.GetComponent<F35VFXController>()?.TriggerCatapultBoost();
        AIManager.Instance?.EnableAICombat();

        // 카메라·HUD 전환
        _fc?.SetFlightTarget(_pc);
        _hud?.SetVisible(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        Debug.Log("[GameMode] → Flight Mode");
    }

    // ── 콕핏 탑승 모드 진입 — 항공기 내부 1인칭, 프리플라이트 수행 후 출격 ────
    public void EnterCockpit(Vector3 zonePos)
    {
        if (IsBoardedInCockpit || IsFlying || _gc == null || _pc == null) return;
        IsBoardedInCockpit = true;

        // 항모 카타펄트가 있으면 항모 방향 정렬. 없으면 사전 배치 위치·회전 그대로 유지.
        if (_boardingCarrier != null)
            _pc.transform.SetPositionAndRotation(zonePos, _boardingCarrier.transform.rotation);

        // 캐릭터 숨기기
        _gc.enabled = false;
        SetCharRenderers(false);

        // 콕핏 탑승 상태를 원격 플레이어에게 알림 (항공기 위치 포함)
        _pc.SendCockpitState(true);

        // 카메라를 콕핏 탑승 모드로 전환 (HUD 미활성)
        _fc?.BeginCockpitBoarding(_pc);

        // 프리플라이트 시퀀스 시작
        var vfx = _pc.GetComponent<F35VFXController>();
        PreflightSystem.Instance?.BeginCockpitPreflight(vfx);
        CockpitInteractionSystem.Instance?.SetActive(true);

        // 커서 해제 — 스위치 클릭을 위해 마우스 이동 허용
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible   = true;

        Debug.Log("[GameMode] → Cockpit Boarding Mode");
    }

    // ── 콕핏 탑승 취소 (ESC) ────────────────────────────────────────────────
    public void ExitCockpit()
    {
        if (!IsBoardedInCockpit) return;
        IsBoardedInCockpit = false;

        PreflightSystem.Instance?.ResetPreflight();
        CockpitInteractionSystem.Instance?.SetActive(false);

        // 콕핏 하차 상태를 원격 플레이어에게 알림
        _pc.SendCockpitState(false);

        _fc?.EndCockpitBoarding();
        _fc?.SetGroundTarget(_gc.transform);

        SetCharRenderers(true);
        _gc.enabled = true;
        _boardingCarrier = null;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        Debug.Log("[GameMode] → Ground Mode (exited cockpit)");
    }

    // ── 지상 모드 복귀 ───────────────────────────────────────────────────────
    // platform: 항모 등 이동 플랫폼의 Transform. 지정 시 캐릭터를 플랫폼 자식으로 배치.
    public void ExitFlight(Vector3 landingPos, Transform platform = null)
    {
        if (!IsFlying || _gc == null || _pc == null) return;
        IsFlying = false;

        float yaw = _pc.transform.eulerAngles.y;

        // 비행 종료를 원격 플레이어에게 즉시 통보 (isFlying=false 패킷)
        _pc.SendFlightState(false);
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

        // 항모 착함 시: 에스코트 AI 순차 착함 시작
        if (platform != null)
            AIManager.Instance?.BeginEscortLanding(platform);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        Debug.Log(platform != null ? $"[GameMode] → Carrier Mode ({platform.name})" : "[GameMode] → Ground Mode");
    }

    // ── 어레스팅 와이어 자동 감속 코루틴 ────────────────────────────────────────
    System.Collections.IEnumerator TrapCoroutine(Transform platform)
    {
        _trapping = true;
        _pc.BeginArrest();
        // 와이어 체결 즉시 에스코트를 자유비행으로 전환 (플레이어 감속에 끌려다니지 않도록)
        AIManager.Instance?.BeginEscortFreeFlightNow();

        // 착함 충격 카메라 쉐이크
        _fc?.TriggerShake(0.7f, 1.8f);

        ShowPrompt(true, "■  ARRESTED  ■  DECELERATING", new Color(1f, 0.75f, 0f, 1f));

        float     deckY = platform.position.y + 0.3f;
        Transform pcT   = _pc.transform;

        while (_pc != null && _pc.CurrentSpeed >= 0.2f)
        {
            Vector3 pos = pcT.position;
            pos.y       = Mathf.MoveTowards(pos.y, deckY, 35f * Time.deltaTime);
            pcT.position = pos;

            float     yaw  = pcT.eulerAngles.y;
            pcT.rotation   = Quaternion.Slerp(pcT.rotation, Quaternion.Euler(0f, yaw, 0f), 6f * Time.deltaTime);

            yield return null;
        }

        if (_pc != null)
        {
            Vector3 snapPos = pcT.position;
            snapPos.y = deckY;
            pcT.SetPositionAndRotation(snapPos, Quaternion.Euler(0f, pcT.eulerAngles.y, 0f));
        }

        // 정지 후 0.4초 대기 (착지 연출)
        yield return new WaitForSeconds(0.4f);

        _pc.EndArrest();
        ExitFlight(platform.position, platform);

        _trapping    = false;
        _trapRoutine = null;
    }

    void StopTrap()
    {
        if (_trapRoutine != null) { StopCoroutine(_trapRoutine); _trapRoutine = null; }
        if (_pc != null) _pc.EndArrest();
        _trapping = false;
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
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 40f);
        rt.sizeDelta        = new Vector2(460f, 58f);
        panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        // ── E 키 박스 (왼쪽) ─────────────────────────────────────────────────
        var keyGO = new GameObject("KeyBox");
        keyGO.transform.SetParent(panel.transform, false);
        var keyRT       = keyGO.AddComponent<RectTransform>();
        keyRT.anchorMin = new Vector2(0f, 0f);
        keyRT.anchorMax = new Vector2(0f, 1f);
        keyRT.pivot     = new Vector2(0f, 0.5f);
        keyRT.offsetMin = new Vector2(10f,  8f);
        keyRT.offsetMax = new Vector2(56f, -8f);
        _keyBoxImage = keyGO.AddComponent<Image>();
        _keyBoxImage.color = new Color(1f, 1f, 1f, 0.18f);

        // 테두리 효과 — 흰색 outline
        var outlineGO = new GameObject("Outline");
        outlineGO.transform.SetParent(keyGO.transform, false);
        var outRT       = outlineGO.AddComponent<RectTransform>();
        outRT.anchorMin = Vector2.zero;
        outRT.anchorMax = Vector2.one;
        outRT.offsetMin = new Vector2(-1.5f, -1.5f);
        outRT.offsetMax = new Vector2( 1.5f,  1.5f);
        outlineGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.55f);
        outlineGO.transform.SetAsFirstSibling();

        var keyTxtGO = new GameObject("KeyTxt");
        keyTxtGO.transform.SetParent(keyGO.transform, false);
        var keyTxtRT       = keyTxtGO.AddComponent<RectTransform>();
        keyTxtRT.anchorMin = Vector2.zero;
        keyTxtRT.anchorMax = Vector2.one;
        keyTxtRT.offsetMin = keyTxtRT.offsetMax = Vector2.zero;
        _keyBoxText = keyTxtGO.AddComponent<Text>();
        _keyBoxText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _keyBoxText.fontSize  = 26;
        _keyBoxText.fontStyle = FontStyle.Bold;
        _keyBoxText.color     = Color.white;
        _keyBoxText.alignment = TextAnchor.MiddleCenter;
        _keyBoxText.text      = "E";

        // ── 액션 텍스트 (키 박스 오른쪽) ────────────────────────────────────
        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(panel.transform, false);
        var tRT = txtGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero;
        tRT.anchorMax = Vector2.one;
        tRT.offsetMin = new Vector2(70f, 0f);
        tRT.offsetMax = new Vector2(-8f, 0f);
        _promptText = txtGO.AddComponent<Text>();
        _promptText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _promptText.fontSize  = 22;
        _promptText.fontStyle = FontStyle.Bold;
        _promptText.color     = new Color(0f, 1f, 0.5f, 1f);
        _promptText.alignment = TextAnchor.MiddleLeft;

        _promptCanvas.enabled = false;
    }

    // keyLabel: "E" = E키 박스 표시, "" = 키 박스 숨김 (ESC·경고 메시지 등)
    void ShowPrompt(bool show, string msg = "", Color color = default, string keyLabel = "E")
    {
        if (_promptCanvas == null) return;
        _promptCanvas.enabled = show;

        bool showKey = show && !string.IsNullOrEmpty(keyLabel);
        if (_keyBoxImage != null) _keyBoxImage.transform.parent.gameObject.SetActive(showKey);
        if (_keyBoxText  != null) _keyBoxText.text = keyLabel;

        if (_promptText != null)
        {
            _promptText.text  = msg;
            _promptText.color = color == default ? new Color(0f, 1f, 0.5f, 1f) : color;
            // 키 박스가 없으면 텍스트를 패널 전체 너비로
            var tRT = _promptText.GetComponent<RectTransform>();
            tRT.offsetMin = showKey ? new Vector2(70f, 0f) : new Vector2(10f, 0f);
        }
    }
}
