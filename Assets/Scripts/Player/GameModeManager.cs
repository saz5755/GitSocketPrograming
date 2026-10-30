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
    /// <summary>격추·추락·사출 후 재출격 전까지 (PilotStatus 가 관리)</summary>
    public bool IsDead            { get; private set; }
    /// <summary>비행 시작 시각 — 발진 직후 충돌 판정 유예 등에 사용</summary>
    public float FlightStartTime  { get; private set; }
    public PlayerController LocalPC => _pc;

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
        _fc  = FindAnyObjectByType<FlightCamera>();
        _hud = FindAnyObjectByType<FlightHUD>();
        _charRenderers = gc.GetComponentsInChildren<Renderer>(true);

        // 프리플라이트 시스템 초기화
        if (GetComponent<PreflightSystem>() == null)
        {
            var pf = gameObject.AddComponent<PreflightSystem>();
            pf.Initialize(LaunchOfficerNPC.Instance);
        }

        // 조종사 생존 상태 (피격 귀속·추락·사출·재출격)
        if (GetComponent<PilotStatus>() == null)
            gameObject.AddComponent<PilotStatus>();
        // 익수자 구조 (갑판 밖으로 떨어지면 구조정 출동)
        if (GetComponent<ManOverboardRescue>() == null)
            gameObject.AddComponent<ManOverboardRescue>();
        // 킬 피드·사망 화면·스코어보드
        if (CombatFeedUI.Instance == null)
            new GameObject("CombatFeed_Canvas").AddComponent<CombatFeedUI>();
        // 훈련 체크리스트 + 위협 코칭 (RWR 경보 시 할 일 안내)
        if (FlightCoach.Instance == null)
            new GameObject("FlightCoach").AddComponent<FlightCoach>();
        // 조종사 G 내성(그레이아웃·G-LOC) + 음성 경고
        if (GLocSystem.Instance == null)
            new GameObject("GLoc_Overlay").AddComponent<GLocSystem>();
        if (GetComponent<FlightVoiceWarnings>() == null)
            gameObject.AddComponent<FlightVoiceWarnings>();
        // 부위별 손상 HUD
        if (FindAnyObjectByType<DamageHUD>() == null)
            new GameObject("DamageHUD_Canvas").AddComponent<DamageHUD>();

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
        _respawnPos = charStart;
        ApplyGroundMode(charStart, 0f);

        Debug.Log($"[GameMode] Init complete. _pc={(pc != null ? pc.name : "null (공유 항공기 대기)")}");
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
        // 공유 항공기 탑승 상태도 재전송 — 새 입장자가 어느 항공기인지 알 수 있도록
        if (IsBoardedInCockpit || IsFlying)
        {
            int id = _pc?.GetComponent<NetworkAircraft>()?.networkId ?? -1;
            if (id >= 0) NetworkManager.Instance?.socketClient?.SendBoardAircraft(id);
        }

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
        // 사망 중에는 입력을 PilotStatus(재출격)가 처리한다 — 탑승·하차 키 무시
        if (IsDead) { ShowPrompt(false); return; }

        // ── 비행 중: 착함·착륙 존 처리 ──────────────────────────────────────
        if (IsFlying)
        {
            if (_activeZone == null) { ShowPrompt(false); return; }

            if (_activeZone.ZoneType == AircraftZone.Type.Carrier)
            {
                if (_trapping)
                {
                    ShowPrompt(true, "■  ARRESTED  ■  SKIP DECEL", new Color(1f, 0.75f, 0f, 1f), keyLabel: "F");
                    if (Input.GetKeyDown(KeyCode.F))
                    {
                        StopTrap();
                        // 공중에서 SKIP 시 항공기를 갑판 Y로 먼저 스냅
                        if (_pc != null && _activeZone != null)
                        {
                            float clearance = ArrestingWireSystem.Instance?.LandingClearance ?? 1.5f;
                            var p = _pc.transform.position;
                            p.y = _activeZone.transform.position.y + clearance;
                            _pc.transform.position = p;
                        }
                        ExitFlight(_activeZone.transform.position, _activeZone.transform);
                    }
                    return;
                }
                float kph     = _pc.CurrentSpeed * 3.6f;
                bool  waveOff = kph > 360f;
                bool  stopped = kph < 20f;
                if (waveOff)
                {
                    ShowPrompt(true, $"WAVE OFF  {kph:F0} KPH — REDUCE SPEED", new Color(1f, 0.2f, 0.1f, 1f));
                }
                else if (stopped)
                {
                    ShowPrompt(true, "EXIT AIRCRAFT", new Color(0f, 1f, 0.5f, 1f), keyLabel: "F");
                    if (Input.GetKeyDown(KeyCode.F))
                        ExitFlight(_activeZone.transform.position, _activeZone.transform);
                }
                else
                {
                    ShowPrompt(true, $"AUTO TRAP  {kph:F0} KPH", new Color(0f, 1f, 0.5f, 1f));
                }
                return;
            }

            if (_activeZone.ZoneType == AircraftZone.Type.Landing)
            {
                float kph = _pc != null ? _pc.CurrentSpeed * 3.6f : 999f;
                if (kph < 20f)
                {
                    ShowPrompt(true, "EXIT AIRCRAFT", keyLabel: "F");
                    if (Input.GetKeyDown(KeyCode.F))
                        ExitFlight(_activeZone.transform.position);
                }
                else
                {
                    ShowPrompt(true, $"SLOW DOWN  {kph:F0} KPH", new Color(1f, 0.6f, 0f, 1f));
                }
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
                _boardingCarrier ??= FindAnyObjectByType<CarrierController>();
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

            if (boardedPC.GetComponent<AIBotController>() != null)
            {
                if (_pc != null)
                    AIManager.Instance?.TakeOverEscortBot(boardedPC, _pc);  // 기체 간 교환
                else
                    AIManager.Instance?.BoardEscortFromGround(boardedPC);   // 지상에서 첫 탑승
            }

            // 이전 항공기의 isLocalPlayer를 해제해야 ArrestingWireSystem._local이 새 기체로 재스캔됨
            if (_pc != null && _pc != boardedPC)
                _pc.isLocalPlayer = false;

            _pc              = boardedPC;
            _pc.isLocalPlayer = true;
            _boardingCarrier = FindAnyObjectByType<CarrierController>();
            EnterCockpit(_pc.transform.position);
        }

        ShowPrompt(false);
    }

    // ── 비행 모드 진입 ───────────────────────────────────────────────────────
    public void EnterFlight(Vector3 boardingPos)
    {
        if (IsFlying || _gc == null || _pc == null) return;
        IsFlying = true;
        FlightStartTime = Time.time;

        // 캐릭터 숨기기
        _gc.enabled = false;
        SetCharRenderers(false);

        // 항공기: 탑승 존 위치에 배치 후 활성화
        // 항모 카타펄트: 항모 방향으로 정렬 후 초기 속도 부여. 없으면 사전 배치 회전 유지.
        Quaternion launchRot = _boardingCarrier != null
            ? _boardingCarrier.transform.rotation
            : _pc.transform.rotation;
        _pc.transform.SetPositionAndRotation(boardingPos, launchRot);

        // 서버의 isBoardedInCockpit을 false로 초기화해야 원격 클라이언트가 항공기 뷰로 전환됨
        _pc.SendCockpitState(false);
        Debug.Log($"[GameMode] EnterFlight: sent CockpitState(false) at {boardingPos}");

        // PC가 씬에서 already-enabled 상태이면 enabled=true가 no-op → OnEnable 미발동 → OnMoveAck 미구독
        // 강제 disable→enable 사이클로 OnEnable을 반드시 발동시킨다
        // ExitFlight에서 비활성한 VFX도 함께 복원
        var vfxOnEnter = _pc.GetComponent<F35VFXController>();
        if (vfxOnEnter != null) vfxOnEnter.enabled = true;
        _pc.enabled = false;
        _pc.isLocalPlayer = true;
        _pc.enabled = true;

        // 갑판 급유 마무리 — 프리플라이트를 건너뛰었거나 급유가 덜 끝났어도 만탱크로 발진
        if (_deckRefuel != null) { StopCoroutine(_deckRefuel); _deckRefuel = null; }
        if (!_pc.IsFuelFull)
        {
            _pc.Refuel();
            CombatFeedUI.Instance?.ShowNotice("DECK REFUEL COMPLETE  ·  FUEL 100%");
        }

        // 캐리어 유무와 관계없이 프리플라이트 완료 후 발진이면 항상 카타펄트
        float catapultSpd = _boardingCarrier != null ? _boardingCarrier.CatapultSpeed : 72f;
        _boardingCarrier = null;
        _pc.StartCatapult(catapultSpd);
        _pc.GetComponent<F35VFXController>()?.TriggerCatapultBoost();
        // 공유 항공기: 이제 _pc가 확정됐으므로 에스코트 리더 등록 후 전투 시작
        AIManager.Instance?.SetLocalPlayerAircraft(_pc);
        AIManager.Instance?.EnableAICombat();

        // 카메라·HUD 전환
        _fc?.SetFlightTarget(_pc);
        _hud?.BindLocalPlayer(_pc);
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

        // 공유 항공기: 어느 항공기를 점령했는지 전체 브로드캐스트
        int boardedId = _pc.GetComponent<NetworkAircraft>()?.networkId ?? -1;
        if (boardedId >= 0) NetworkManager.Instance?.socketClient?.SendBoardAircraft(boardedId);

        // 카메라를 콕핏 탑승 모드로 전환 (HUD 미활성)
        _fc?.BeginCockpitBoarding(_pc);

        // 프리플라이트 시퀀스 시작
        var vfx = _pc.GetComponent<F35VFXController>();
        PreflightSystem.Instance?.BeginCockpitPreflight(vfx);

        // 갑판 급유 — 프리플라이트(APU·엔진 시동) 동안 갑판 요원이 연료를 채운다
        if (_deckRefuel != null) StopCoroutine(_deckRefuel);
        if (!_pc.IsFuelFull) _deckRefuel = StartCoroutine(DeckRefuelRoutine(_pc));
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

        // LEAVE_AIRCRAFT 전송: 원격 _boardedAircraftId / _remoteBoarded 해제 → 캐릭터 가시성 복구
        int exitingId = _pc?.GetComponent<NetworkAircraft>()?.networkId ?? -1;
        if (exitingId >= 0) NetworkManager.Instance?.socketClient?.SendLeaveAircraft(exitingId);

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

        // VFX 중지 — _pc.enabled=false 이후에도 파티클·불꽃이 남지 않도록
        var vfx = _pc.GetComponent<F35VFXController>();
        if (vfx != null) { vfx.SetEngineIdle(false); vfx.enabled = false; }

        float yaw = _pc.transform.eulerAngles.y;

        // 공유 항공기 점령 해제 — 원격 클라이언트가 해당 항공기를 idle로 전환
        int leavingId = _pc.GetComponent<NetworkAircraft>()?.networkId ?? -1;
        if (leavingId >= 0) NetworkManager.Instance?.socketClient?.SendLeaveAircraft(leavingId);

        // 비행 종료를 원격 플레이어에게 즉시 통보 (isFlying=false 패킷)
        _pc.SendFlightState(false);
        _pc.enabled = false;

        // 다음 사이클을 위해 캐리어 참조 초기화 (EnterCockpit에서 재할당)
        _boardingCarrier = null;

        // 캐릭터를 기체 옆 갑판 위에 내린다.
        // 예전엔 착함 존 중심(landingPos)에 그대로 놓았는데, 와이어 없이 감속 정지하면 기체가 그 자리에 서 있어
        // 캐릭터 캡슐이 기체 콜라이더 안에 생기고 → 밀려나며 얇은 갑판 콜라이더를 뚫고 아래로 떨어졌다.
        Vector3 spawnPos = FindDismountPoint(_pc.transform, landingPos);
        if (platform != null)
        {
            // 항모 갑판 위 착함: 캐릭터를 항모 좌표계 기준으로 부착
            _gc.transform.SetParent(platform, true);
        }
        // CharacterController 가 켜진 채 순간이동하면 내부 위치가 어긋날 수 있다 — 끈 채 옮기고 물리에 즉시 반영
        var gcc = _gc.GetComponent<CharacterController>();
        if (gcc != null) gcc.enabled = false;
        _gc.transform.SetPositionAndRotation(spawnPos, Quaternion.Euler(0f, yaw, 0f));
        Physics.SyncTransforms();
        if (gcc != null) gcc.enabled = true;
        _gc.InitYaw(yaw);
        SetCharRenderers(true);
        _gc.enabled = true;

        // 카메라·HUD 전환
        _fc?.SetGroundTarget(_gc.transform);
        _hud?.SetVisible(false);

        // 와이어 체결 후 하차 시: 저장된 경로로 에스코트 AI 순차 착함 시작
        // platform != null (Carrier존) 또는 HasArrestInfo (Landing존에서 와이어 체결 후 하차) 모두 처리
        if (platform != null || (AIManager.Instance != null && AIManager.Instance.HasArrestInfo))
        {
            var ai = AIManager.Instance;
            if (ai != null && !ai.IsHost)
            {
                // 게스트: 에스코트 AI 는 호스트에 있으므로 호스트에게 착함 시작을 요청
                var sc = NetworkManager.Instance?.socketClient;
                if (ai.HasArrestInfo)
                {
                    var (dir, wire) = ai.GetArrestInfo();
                    sc?.SendArrestEvent(1, dir, wire);
                }
                else sc?.SendArrestEvent(2, Vector3.zero, Vector3.zero);
            }
            else ai?.BeginEscortLandingAfterDismount();
        }

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
        if (AIManager.Instance != null && !AIManager.Instance.IsHost)
            NetworkManager.Instance?.socketClient?.SendArrestEvent(0, Vector3.zero, Vector3.zero);

        // 착함 충격 카메라 쉐이크
        _fc?.TriggerShake(0.7f, 1.8f);

        ShowPrompt(true, "■  ARRESTED  ■  DECELERATING", new Color(1f, 0.75f, 0f, 1f));

        float clearance = ArrestingWireSystem.Instance != null ? ArrestingWireSystem.Instance.LandingClearance : 1.5f;
        float     deckY = platform.position.y + clearance;
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

    // ArrestingWireSystem에서 와이어 정지 확인 시 호출.
    // TrapCoroutine(루프 탈출 대기)을 강제 종료하고 ExitFlight를 직접 실행.
    public void OnArrestWireStopped(Vector3 landingPos, Transform platform)
    {
        if (!IsFlying) return;
        StopTrap();
        StartCoroutine(DelayedExitAfterArrest(landingPos, platform));
    }

    System.Collections.IEnumerator DelayedExitAfterArrest(Vector3 landingPos, Transform platform)
    {
        yield return new WaitForSeconds(0.4f);
        ExitFlight(landingPos, platform);
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────
    // ── 사망 / 재출격 ───────────────────────────────────────────────────────
    Vector3 _respawnPos;

    /// <summary>조종사 사망 — 비행 상태를 끝내고 무장·HUD·존 처리를 멈춘다 (캐릭터는 아직 숨긴 채).</summary>
    public void EnterDeathState()
    {
        if (IsDead) return;
        IsDead   = true;
        IsFlying = false;
        if (_trapping) StopTrap();
        _activeZone = null;
        _hud?.SetVisible(false);
        ShowPrompt(false);
    }

    /// <summary>재출격 — 조종사를 항모 갑판(최초 스폰 위치)으로 복귀시킨다. 기체 정비·재배치는 PilotStatus 가 먼저 수행.</summary>
    public void RespawnOnDeck()
    {
        if (!IsDead || _gc == null) return;
        IsDead = false;

        if (_pc != null)
        {
            var vfx = _pc.GetComponent<F35VFXController>();
            if (vfx != null) { vfx.SetEngineIdle(false); vfx.enabled = false; }

            int id = _pc.GetComponent<NetworkAircraft>()?.networkId ?? -1;
            if (id >= 0) NetworkManager.Instance?.socketClient?.SendLeaveAircraft(id);
            _pc.SendFlightState(false);
            _pc.enabled = false;
        }

        // 항모는 정적 배치 오브젝트 — 와이어 시스템 부모가 항공모함 루트
        var carrier = ArrestingWireSystem.Instance != null ? ArrestingWireSystem.Instance.transform.parent : null;
        if (carrier != null) _gc.transform.SetParent(carrier, true);
        ApplyGroundMode(_respawnPos, 0f);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        Debug.Log("[GameMode] → Respawned on carrier deck");
    }

    /// <summary>바다에 빠진 지상 캐릭터를 구조 후 갑판(최초 스폰 위치)으로 복귀시킨다.</summary>
    public void RescueCharacterToDeck()
    {
        if (_gc == null) return;
        var carrier = ArrestingWireSystem.Instance != null ? ArrestingWireSystem.Instance.transform.parent : null;
        _gc.EndSwim();
        // CharacterController 가 순간이동을 되돌리지 않도록 잠시 끈 채 옮긴다
        var cc = _gc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        if (carrier != null) _gc.transform.SetParent(carrier, true);
        ApplyGroundMode(_respawnPos, 0f);
        if (cc != null) cc.enabled = true;
        Debug.Log("[GameMode] → Rescued, back on deck");
    }

    void ApplyGroundMode(Vector3 charPos, float yaw)
    {
        IsFlying = false;

        if (_pc != null) _pc.enabled = false;

        _gc.transform.position = charPos;
        _gc.InitYaw(yaw);
        SetCharRenderers(true);
        _gc.enabled = true;

        _hud?.SetVisible(false);
        _fc?.SetGroundTarget(_gc.transform);
    }

    // ── 갑판 급유 ─────────────────────────────────────────────────────────
    const float DeckRefuelSeconds = 6f;   // 빈 탱크 → 만탱크 (프리플라이트 시간과 비슷하게)
    Coroutine _deckRefuel;

    System.Collections.IEnumerator DeckRefuelRoutine(PlayerController pc)
    {
        CombatFeedUI.Instance?.ShowNotice($"DECK REFUELING  ·  FUEL {pc.CurrentFuel / pc.MaxFuel * 100f:F0}%");
        float rate = pc.MaxFuel / DeckRefuelSeconds;
        float noticeT = 0f;
        while (pc != null && IsBoardedInCockpit && !pc.IsFuelFull)
        {
            pc.AddFuel(rate * Time.deltaTime);
            noticeT += Time.deltaTime;
            if (noticeT >= 1.2f) { noticeT = 0f; CombatFeedUI.Instance?.ShowNotice($"DECK REFUELING  ·  FUEL {pc.CurrentFuel / pc.MaxFuel * 100f:F0}%"); }
            yield return null;
        }
        if (pc != null && pc.IsFuelFull) CombatFeedUI.Instance?.ShowNotice("DECK REFUEL COMPLETE  ·  FUEL 100%");
        _deckRefuel = null;
    }

    // ── 하차 위치: 조종석 왼편(실제 조종사 사다리 쪽)부터 기체 주변 갑판의 빈 자리를 찾는다 ─────
    static readonly Vector2[] DismountOffsets =
    {
        new(-4.5f, 1.5f), new(4.5f, 1.5f), new(-6.5f, -1f), new(6.5f, -1f),
        new(-4f, 8f), new(4f, 8f), new(0f, -10f), new(-9f, 3f), new(9f, 3f),
    };
    readonly Collider[] _overlap = new Collider[16];

    Vector3 FindDismountPoint(Transform aircraft, Vector3 fallback)
    {
        var cc = _gc.GetComponent<CharacterController>();
        float r = cc != null ? cc.radius : 0.4f, h = cc != null ? cc.height : 1.8f;
        float feetOffset = cc != null ? cc.center.y - h * 0.5f : 0f;   // 피벗 → 발바닥
        Vector3 fwd = aircraft.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        foreach (var o in DismountOffsets)
        {
            Vector3 probe = aircraft.position + right * o.x + fwd * o.y;
            if (!TryDeck(probe, aircraft, out Vector3 ground)) continue;
            Vector3 feet = ground + Vector3.up * 0.08f;
            // 캡슐이 기체·구조물과 겹치지 않는 자리만
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (r + 0.02f), feet + Vector3.up * (h - r), r * 0.95f,
                                                   _overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < n; i++)
                if (!_overlap[i].transform.IsChildOf(_gc.transform)) { blocked = true; break; }
            if (!blocked) return feet - Vector3.up * feetOffset;
        }
        Debug.LogWarning("[GameMode] 하차: 기체 주변에 빈 갑판을 찾지 못해 존 위치 사용");
        return fallback + Vector3.up * 0.05f;
    }

    // probe 위에서 아래로 — 기체·캐릭터 자신을 제외한 첫 지면 (해수면 근처는 갑판 아님)
    bool TryDeck(Vector3 probe, Transform aircraft, out Vector3 ground)
    {
        ground = default;
        var hits = Physics.RaycastAll(probe + Vector3.up * 12f, Vector3.down, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(aircraft) || hit.transform.IsChildOf(_gc.transform)) continue;
            if (hit.distance < best) { best = hit.distance; ground = hit.point; found = true; }
        }
        var ocean = OceanSurface.Instance;
        if (found && ocean != null && ground.y < ocean.seaLevel + 1f) return false;
        return found;
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
