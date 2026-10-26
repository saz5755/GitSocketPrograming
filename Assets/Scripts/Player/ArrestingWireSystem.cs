using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 항공모함 어레스팅 와이어 시스템 (3-wire).
/// 항공모함 루트 오브젝트에 부착.
/// 비주얼은 ArrestingCable(펜던트 케이블 + 시브/서포트 하드웨어), 기체 쪽은 TailhookRig가 담당한다.
/// </summary>
public class ArrestingWireSystem : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("동작 튜닝 데이터")]
    [Tooltip("WireSystemSO 에셋. Project 우클릭 → Create → KF21 → Carrier → Wire System")]
    [SerializeField] WireSystemSO _config;

    // SO 미할당 시 폴백 인스턴스 — 한 번만 생성
    static WireSystemSO s_fallbackConfig;
    WireSystemSO Cfg
    {
        get
        {
            if (_config != null) return _config;
            if (s_fallbackConfig == null)
            {
                s_fallbackConfig = ScriptableObject.CreateInstance<WireSystemSO>();
                // OnDrawGizmos 등 에디터 비-플레이 호출에서 경고가 반복되지 않도록 플레이 모드에서만 출력
                if (Application.isPlaying)
                    Debug.LogWarning(
                        $"[ArrestingWireSystem] '{name}' has no WireSystemSO assigned — using built-in defaults. " +
                        "Create one via Project window → Right Click → Create → KF21 → Carrier → Wire System."
                    );
            }
            return s_fallbackConfig;
        }
    }

    public static ArrestingWireSystem Instance { get; private set; }

    // ── 내부 상태 ──────────────────────────────────────────────────────────────
    class WireState
    {
        public Transform      root;
        public ArrestingCable cable;
        public Renderer       disc;
        public Material       discMat;
        public Light          light;

        public float   wireWorldY;

        // 케이블을 끌고 가는 기체 — 플레이어(caught) 또는 착함 중인 에스코트
        public Transform   hookOwner;
        public TailhookRig hook;

        // swept 감지 — 프레임 스킵 방지
        public float prevLocalX;
        public float prevLocalY;
        public float prevLocalZ;
        public bool  prevPosValid;

        // 스무스 착지
        public float deckTargetY;   // 체결 시점 Raycast로 탐지한 갑판 Y
        public float landT;         // 체결 후 경과 시간

        public bool  caught;
        public bool  stopped;
        public float resetTimer;
    }

    readonly WireState[] _wires = new WireState[3];
    PlayerController     _local;
    FlightCamera         _flightCam;

    // 케이블/하드웨어 공유 재질
    Material _cableMat, _paintMat, _steelMat, _darkMat;
    float    _rigScanTimer;

    // 체결 직후 기수 숙임 스프링 (도)
    float _noseDip, _noseDipVel;

    // 플레이어 착함 시 에스코트 경로 설정용
    Vector3 _arrestApproachDir;
    Vector3 _arrestWirePos;

    // 와이어 상태 색은 WireSystemSO에서 가져옴 — 에디터에서 직접 튜닝 가능

    // GameModeManager.TrapCoroutine이 플레이어 착함 Y를 맞출 때 참조
    public float LandingClearance => Cfg.landingClearance;

    /// <summary>착함 진행 방향 (선미 → 선수, 사선 갑판 각 포함) — 테일훅 전개 판정용</summary>
    public Vector3 LandingDirection => transform.rotation * Quaternion.Euler(0f, Cfg.deckAngleDeg, 0f) * Vector3.back;
    /// <summary>가운데 와이어 위치</summary>
    public Vector3 WireCenter => transform.TransformPoint(Cfg.wire2LocalPos);

    // ── 생명주기 ───────────────────────────────────────────────────────────────
    void Awake()
    {
        // 중복 부착 방어 — 두 개가 모두 동작하면 와이어가 겹쳐 생성되고 체결도 이중으로 일어난다.
        // WireSystemSO가 할당된 쪽을 남긴다 (Start 전이므로 비활성화하면 와이어를 만들지 않음).
        if (Instance != null && Instance != this && Instance.isActiveAndEnabled)
        {
            var drop = (_config != null && Instance._config == null) ? Instance : this;
            Debug.LogWarning($"[ArrestingWireSystem] 중복 컴포넌트 감지 — '{drop.name}'의 컴포넌트를 비활성화합니다. 씬에서 하나를 제거하세요.");
            drop.enabled = false;
            if (drop == this) return;
        }
        Instance = this;
    }

    void Start()
    {
        BuildMaterials();
        HideModelWires();

        Vector3[] pos = { Cfg.wire1LocalPos, Cfg.wire2LocalPos, Cfg.wire3LocalPos };
        for (int i = 0; i < 3; i++)
            _wires[i] = BuildWire(i + 1, pos[i]);

        // WireSystemSO의 Y 값이 모델 피벗 기준으로 부정확할 수 있으므로
        // 각 와이어 XZ 위치에서 레이캐스트로 실제 갑판 면 Y를 탐지해 자동 보정.
        SnapWiresToDeckHeight();
    }

    // 와이어 루트를 실제 갑판 높이에 자동 배치.
    // 1차: 레이캐스트(MeshCollider 필요). 2차: 자식 Renderer Bounds(MeshCollider 없는 모델 대응).
    void SnapWiresToDeckHeight()
    {
        var renderers = GetComponentsInChildren<Renderer>();

        foreach (var ws in _wires)
        {
            if (ws?.root == null) continue;

            Vector3 xzPos = ws.root.position;
            int n = Physics.RaycastNonAlloc(
                new Vector3(xzPos.x, 500f, xzPos.z), Vector3.down,
                _deckHits, 1000f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float deckY = float.MinValue;
            for (int k = 0; k < n; k++)
                if (_deckHits[k].point.y > deckY) deckY = _deckHits[k].point.y;

            // 레이캐스트가 와이어 기본 Y + 1m 이하(= 바다 수면 등 콜라이더 미탐지)면
            // 자식 Renderer Bounds로 갑판 면 Y를 탐지한다.
            // 갑판 표면은 가장 낮은 maxY(> -1m) 렌더러 — 갑판 위 구조물보다 낮기 때문.
            if (deckY <= ws.root.position.y + 1f)
            {
                Vector3 localWZ = transform.InverseTransformPoint(xzPos);
                float rMinDeckY = float.MaxValue;
                foreach (var r in renderers)
                {
                    if (r.bounds.size.y > 3f) continue;
                    Vector3 rc = transform.InverseTransformPoint(r.bounds.center);
                    if (Mathf.Abs(rc.z - localWZ.z) > 15f) continue;
                    // 갑판 범위(-1m ~ 5m)의 maxY 중 최솟값 = 갑판 표면 (선체 아래 제외)
                    if (r.bounds.max.y < -1f || r.bounds.max.y > 5f) continue;
                    if (r.bounds.max.y < rMinDeckY) rMinDeckY = r.bounds.max.y;
                }
                if (rMinDeckY < float.MaxValue) deckY = rMinDeckY;
            }

            if (deckY <= float.MinValue) continue;

            Vector3 lp = ws.root.localPosition;
            lp.y = transform.InverseTransformPoint(
                       new Vector3(xzPos.x, deckY, xzPos.z)).y;
            ws.root.localPosition = lp;
            ws.wireWorldY         = deckY;
            Debug.Log($"[ArrestWire] Snapped to deck Y={deckY:F2} (localY={lp.y:F2})");
        }
    }

    // 항모 모델에 들어 있는 샘플 와이어 메쉬를 끈다 — 런타임 와이어와 위치·개수가 달라 겹쳐 보임
    void HideModelWires()
    {
        if (Cfg.hideCarrierMeshes == null || Cfg.hideCarrierMeshes.Length == 0) return;
        Transform carrier = transform.parent != null ? transform.parent : transform;
        foreach (var r in carrier.GetComponentsInChildren<MeshRenderer>(true))
            if (System.Array.IndexOf(Cfg.hideCarrierMeshes, r.gameObject.name) >= 0)
                r.enabled = false;
    }

    // ── 재질 ──────────────────────────────────────────────────────────────────
    void BuildMaterials()
    {
        Shader rope = Shader.Find("Custom/WireRope");
        Shader lit  = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (rope == null) Debug.LogWarning("[ArrestingWireSystem] Custom/WireRope 셰이더 없음 — URP Lit으로 대체");

        _cableMat = new Material(rope != null ? rope : lit) { name = "PendantCable" };
        if (rope == null) SetLit(_cableMat, new Color(0.5f, 0.49f, 0.47f), 1f, 0.6f);

        _steelMat = Cfg.wireSteelMaterial != null
            ? Cfg.wireSteelMaterial
            : SetLit(new Material(lit) { name = "GearSteel" }, new Color(0.56f, 0.55f, 0.53f), 1f, 0.68f);
        _paintMat = SetLit(new Material(lit) { name = "GearPaint" }, new Color(0.15f, 0.155f, 0.16f), 0.35f, 0.32f);
        _darkMat  = SetLit(new Material(lit) { name = "GearDark" },  new Color(0.02f, 0.02f, 0.022f), 0f, 0.12f);
    }

    static Material SetLit(Material m, Color c, float metallic, float smoothness)
    {
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    // ── 와이어 한 줄 생성 ──────────────────────────────────────────────────────
    WireState BuildWire(int idx, Vector3 localPos)
    {
        var go = new GameObject($"ArrestWire_{idx}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, Cfg.deckAngleDeg, 0f);

        var ws = new WireState { root = go.transform };
        ws.cable = new ArrestingCable(go.transform, Cfg.wireWidth, _cableMat, _paintMat, _steelMat, _darkMat);

        BuildZoneDisc(ws);      // 디버그용 존 디스크
        BuildZoneLabel(ws, idx);

        ws.wireWorldY   = go.transform.position.y;
        ws.prevPosValid = false;

        return ws;
    }

    // ── 존 디스크 (디버그) ─────────────────────────────────────────────────────
    void BuildZoneDisc(WireState ws)
    {
        if (!Cfg.showZoneDisc) return;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "ZoneDisc";
        disc.transform.SetParent(ws.root, false);
        disc.transform.localPosition = new Vector3(0f, Cfg.triggerHeight * 0.5f, 0f);
        disc.transform.localRotation = Quaternion.identity;
        disc.transform.localScale    = new Vector3(Cfg.wireWidth, Cfg.triggerHeight, 1f);
        Destroy(disc.GetComponent<Collider>());

        var rend = disc.GetComponent<Renderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows    = false;
        Material mat = Cfg.zoneDiscMaterial != null
            ? new Material(Cfg.zoneDiscMaterial)   // 와이어별 색 독립 제어
            : MakeTransparentMaterial(Cfg.zoneColorAvailable);
        mat.color = Cfg.zoneColorAvailable;
        rend.material = mat;

        var light = disc.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = Cfg.zoneColorAvailable;
        light.intensity = 3f;
        light.range     = Cfg.wireWidth * 0.8f;

        ws.disc    = rend;
        ws.discMat = mat;
        ws.light   = light;
    }

    // ── 라벨 ───────────────────────────────────────────────────────────────────
    void BuildZoneLabel(WireState ws, int idx)
    {
        if (!Cfg.showZoneLabel) return;
        var labelGO = new GameObject("ZoneLabel");
        labelGO.transform.SetParent(ws.root, false);
        labelGO.transform.localPosition = new Vector3(0f, Cfg.triggerHeight + 1.5f, 0f);
        labelGO.transform.localScale    = Vector3.one * 0.025f;

        var canvas = labelGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        labelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 50f);

        var txtGO = new GameObject("Txt");
        txtGO.transform.SetParent(labelGO.transform, false);
        var tRT = txtGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;

        var txt = txtGO.AddComponent<Text>();
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = 24;
        txt.fontStyle = FontStyle.Bold;
        txt.color     = new Color(0.8f, 0.2f, 1.0f, 1f);
        txt.alignment = TextAnchor.MiddleCenter;
        txt.text      = $"ARREST WIRE {idx}";

        labelGO.AddComponent<ZoneLabelBillboard>();
    }

    // ── 케이블 갱신 ────────────────────────────────────────────────────────────
    // 훅이 없는 기체(원격 등)는 기체 후방 하부를 훅 위치로 근사
    static Vector3 HookPoint(WireState ws)
    {
        if (ws.hook != null) return ws.hook.TipWorld;
        return ws.hookOwner.position - ws.hookOwner.forward * 3.4f;
    }

    void Engage(WireState ws, Transform owner)
    {
        ws.hookOwner = owner;
        ws.hook      = TailhookRig.Ensure(owner);
        if (ws.hook != null) ws.hook.SetEngaged(true);
        ws.cable.Engage(HookPoint(ws));
    }

    void ReleaseCable(WireState ws)
    {
        if (ws.hook != null) ws.hook.SetEngaged(false);
        ws.hookOwner = null;
        ws.hook      = null;
        ws.cable.Release();
    }

    void UpdateCable(WireState ws)
    {
        if (ws.cable.CurrentState == ArrestingCable.State.Engaged && ws.hookOwner == null)
            ReleaseCable(ws);   // 끌고 가던 기체가 사라짐
        Vector3 hook = ws.hookOwner != null ? HookPoint(ws) : Vector3.zero;
        ws.cable.Tick(Time.deltaTime, hook);
    }

    // 항모 주변 항공기에 테일훅 리그 부착 (1초 간격 — 새로 스폰된 기체 대응)
    void EnsureHookRigs()
    {
        _rigScanTimer -= Time.deltaTime;
        if (_rigScanTimer > 0f) return;
        _rigScanTimer = 1f;
        foreach (var pc in PlayerController.All)
            if (pc != null) TailhookRig.Ensure(pc.transform);
        foreach (var e in EscortAI.AllEscorts)
            if (e != null) TailhookRig.Ensure(e.transform);
    }

    // ── Update ─────────────────────────────────────────────────────────────────
    void Update()
    {
        EnsureHookRigs();
        UpdateEscortEngagements();

        if (_local == null || !_local.isLocalPlayer)
        {
            _local = null;
            foreach (var pc in PlayerController.All)
                if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
            // 에스코트 전투기 탑승 시 PlayerController.All에 누락될 수 있으므로 추가 탐색
            if (_local == null)
            {
                var la = GameModeManager.Instance?.LocalAircraft;
                if (la != null) _local = la.GetComponent<PlayerController>();
            }
            if (_local == null) return;
        }

        float speed = _local.CurrentSpeed;

        // 훅은 와이어 하나만 잡는다 — 감속 중 굴러가며 다음 와이어 위를 지나도 추가 체결 금지
        bool anyCaught = false;
        foreach (var w in _wires) if (w != null && w.caught) { anyCaught = true; break; }

        for (int i = 0; i < _wires.Length; i++)
        {
            var ws = _wires[i];

            if (ws.caught)
            {
                // 체결 중 스무스 착지 처리
                if (!ws.stopped) SmoothLanding(ws);

                // 감속 → 롤백까지 끝나 완전히 멈춰야 정지 처리 (EndArrest가 롤백을 끊지 않도록)
                if (!ws.stopped && (_local.IsArrestSettled || !_local.IsArrested))
                {
                    _local.EndArrest();
                    ws.stopped    = true;
                    ws.resetTimer = Cfg.resetDelay;
                    ReleaseCable(ws);
                    SetColors(ws, Cfg.zoneColorStopped);
                    // 플레이어 경로 저장 후 GameModeManager에 착함 완료 통보
                    AIManager.Instance?.StoreArrestInfo(_arrestApproachDir, _arrestWirePos);
                    GameModeManager.Instance?.OnArrestWireStopped(_local.transform.position, ws.root);
                    Debug.Log($"[ArrestWire] Wire {i + 1}: 정지 → EndArrest");
                }

                if (ws.stopped)
                {
                    ws.resetTimer -= Time.deltaTime;
                    if (ws.resetTimer <= 0f)
                    {
                        ws.caught  = false;
                        ws.stopped = false;
                        SetColors(ws, Cfg.zoneColorAvailable);
                        Debug.Log($"[ArrestWire] Wire {i + 1}: 리셋");
                    }
                }
                continue;
            }

            // TrapCoroutine이 이미 감속 중(_arrested)이면 minCatchSpeed 무시 —
            // 캐리어 존 진입으로 먼저 감속이 시작된 경우에도 와이어가 체결되어야 한다.
            if (anyCaught || (speed < Cfg.minCatchSpeed && !_local.IsArrested))
            {
                // 속도 미달이어도 이전 위치는 갱신 (swept 감지 오작동 방지)
                UpdatePrevPos(ws, ws.root.InverseTransformPoint(_local.transform.position));
                continue;
            }

            // ── Swept 감지 — 고속 비행 시 프레임 스킵 방지 ────────────────────
            // 와이어 로컬 Z=0 평면을 전 프레임과 이번 프레임 사이에 통과했는지 검사
            Vector3 lp = ws.root.InverseTransformPoint(_local.transform.position);
            bool inZone = false;

            // 한 프레임에 40m 이상 이동 = 순간이동(리스폰 등) — 스윕 판정에서 제외
            if (ws.prevPosValid && Mathf.Abs(ws.prevLocalZ - lp.z) < 40f)
            {
                float pz = ws.prevLocalZ;
                float cz = lp.z;
                if ((pz > 0f) != (cz > 0f)) // Z=0 평면 교차 (양방향)
                {
                    // 교차 지점에서의 X·Y 보간
                    float t      = Mathf.Abs(pz) / (Mathf.Abs(pz) + Mathf.Abs(cz));
                    float crossX = Mathf.Lerp(ws.prevLocalX, lp.x, t);
                    float crossY = Mathf.Lerp(ws.prevLocalY, lp.y, t);
                    inZone = Mathf.Abs(crossX) < Cfg.wireWidth * 0.5f &&
                             crossY > -1f && crossY < Cfg.triggerHeight;
                }
            }

            // 저속·정지 상태 대비 일반 볼륨 체크도 병행
            if (!inZone)
            {
                inZone = Mathf.Abs(lp.x) < Cfg.wireWidth    * 0.5f &&
                         lp.y            > -1f &&
                         lp.y            < Cfg.triggerHeight &&
                         Mathf.Abs(lp.z) < Cfg.triggerDepth * 0.5f;
            }

            UpdatePrevPos(ws, lp); // 항상 이전 위치 갱신

            if (!inZone) continue;

            // ── 체결 ─────────────────────────────────────────────────────────
            ws.caught       = true;
            ws.stopped      = false;
            anyCaught       = true;
            ws.wireWorldY   = ws.root.TransformPoint(Vector3.zero).y;
            ws.deckTargetY  = DetectDeckY(_local.transform.position, ws.wireWorldY + Cfg.landingClearance, _local.transform);
            ws.landT        = 0f;
            ws.prevPosValid = false; // 체결 직후 스윕 오감지 방지
            SetColors(ws, Cfg.zoneColorCaught);
            Engage(ws, _local.transform);
            _local.BeginArrest();
            // 체결 충격 — 짧고 강한 카메라 흔들림
            if (_flightCam == null) _flightCam = FindAnyObjectByType<FlightCamera>();
            if (_flightCam != null) _flightCam.TriggerShake(0.45f, 0.55f);
            _noseDip = 0f; _noseDipVel = 0f;
            _arrestApproachDir = _local.transform.forward;
            _arrestWirePos     = ws.root.TransformPoint(Vector3.zero);
            // 와이어 체결 즉시 에스코트를 자유비행으로 전환 (TrapCoroutine 경로와 무관하게 보장)
            AIManager.Instance?.BeginEscortFreeFlightNow();
            // 게스트인 경우 호스트에게 체결 이벤트 전달 — 호스트의 에스코트 AI가 자유비행으로 전환됨
            if (AIManager.Instance == null || !AIManager.Instance.IsHost)
                NetworkManager.Instance?.socketClient?.SendArrestEvent(0, Vector3.zero, Vector3.zero);
            Debug.Log($"[ArrestWire] Wire {i + 1} 체결! 속도={speed:F1} m/s  deckY={ws.deckTargetY:F2}");
        }

        for (int i = 0; i < _wires.Length; i++)
        {
            var ws = _wires[i];
            // ── 어레스팅 와이어 에스코트 체결 감지 ──────────────────────────
            if (!ws.caught) CheckEscortWire(ws, i);
        }
    }

    // 케이블은 모든 기체 이동이 끝난 뒤 갱신 — Update 순서에 따라 훅보다 한 프레임 뒤처지지 않게
    void LateUpdate()
    {
        for (int i = 0; i < _wires.Length; i++)
            if (_wires[i] != null) UpdateCable(_wires[i]);
    }

    // ── Swept 감지 헬퍼 ───────────────────────────────────────────────────────
    void UpdatePrevPos(WireState ws, Vector3 lp)
    {
        ws.prevLocalX   = lp.x;
        ws.prevLocalY   = lp.y;
        ws.prevLocalZ   = lp.z;
        ws.prevPosValid = true;
    }

    // ── 갑판 Y Raycast 탐색 (ConstrainToSurface 동일 로직) ────────────────────
    static readonly RaycastHit[] _deckHits = new RaycastHit[8];
    float DetectDeckY(Vector3 fromPos, float fallbackY, Transform ignoreSelf = null)
    {
        // 위로 30 m에서 하강 레이캐스트 — 와이어 루트 Y가 갑판보다 낮더라도 갑판을 탐지
        int count = Physics.RaycastNonAlloc(
            fromPos + Vector3.up * 30f, Vector3.down,
            _deckHits, 60f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        var skip = ignoreSelf ?? _local?.transform;
        float bestY = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            if (skip != null && _deckHits[k].transform.IsChildOf(skip)) continue;
            if (_deckHits[k].point.y > bestY) bestY = _deckHits[k].point.y;
        }

        // 레이캐스트가 와이어 위치 Y + 1m 이하(= 바다 수면 등)만 탐지하면
        // 자식 Renderer Bounds로 갑판 면 Y를 보완한다 (MeshCollider 없는 모델 대응).
        // 갑판 표면 = 갑판 범위(-1m~5m)에서 가장 낮은 maxY (갑판 위 구조물 제외).
        if (bestY <= fallbackY + 1f)
        {
            float rMinDeckY = float.MaxValue;
            Vector3 fp      = transform.InverseTransformPoint(fromPos);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r.bounds.size.y > 3f) continue;
                Vector3 rc = transform.InverseTransformPoint(r.bounds.center);
                if (Mathf.Abs(rc.z - fp.z) > 20f) continue;
                if (r.bounds.max.y < -1f || r.bounds.max.y > 5f) continue;
                if (r.bounds.max.y < rMinDeckY) rMinDeckY = r.bounds.max.y;
            }
            if (rMinDeckY < float.MaxValue && rMinDeckY > bestY) bestY = rMinDeckY;
        }

        return bestY > float.MinValue ? bestY + Cfg.landingClearance : fallbackY;
    }

    // 에스코트 전용 — clearance 미포함 순수 갑판 면 Y 반환.
    // EscortAI._modelBottomOffset이 피벗 보정을 담당하므로 clearance를 이중 적용하지 않는다.
    float DetectRawDeckY(Vector3 fromPos, float fallbackY, Transform ignoreSelf = null)
    {
        int count = Physics.RaycastNonAlloc(
            fromPos + Vector3.up * 3f, Vector3.down,
            _deckHits, 8f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float bestY = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            if (ignoreSelf != null && _deckHits[k].transform.IsChildOf(ignoreSelf)) continue;
            if (_deckHits[k].point.y > bestY) bestY = _deckHits[k].point.y;
        }
        return bestY > float.MinValue ? bestY : fallbackY;
    }

    void CheckEscortWire(WireState ws, int wireIdx)
    {
        foreach (var escort in EscortAI.AllEscorts)
        {
            if (escort == null || !escort.IsInApproachRun) continue;

            Vector3 lp = ws.root.InverseTransformPoint(escort.transform.position);

            // 에스코트 어프로치 속도(25 m/s)는 낮아 매 프레임 감지로 충분.
            // ws.prevLocal* 은 플레이어 위치이므로 에스코트 swept 감지에 사용하면 오감지 발생.
            // X 범위 체크 제거: 좌우 배치 에스코트는 ±22m까지 X 오프셋 가능,
            // wireWidth * 0.5f(~7-10m) 기준이면 체결 실패. IsInApproachRun이 1차 게이트.
            bool inZone = lp.y > -1f && lp.y < Cfg.triggerHeight &&
                          Mathf.Abs(lp.z) < Cfg.triggerDepth * 0.5f;

            if (!inZone) continue;

            // 에스코트는 순수 갑판 면 Y 전달 — EscortAI._modelBottomOffset이 피벗 보정
            float rawDeckY = DetectRawDeckY(escort.transform.position, ws.wireWorldY, escort.transform);
            escort.OnWireCaught(rawDeckY);
            Debug.Log($"[ArrestWire] Wire {wireIdx + 1} 에스코트 체결: {escort.name}");
            break;
        }
    }

    // ── 스무스 착지 ──────────────────────────────────────────────────────────
    // 체결 후 전투기를 갑판에 내려앉히고(주바퀴 → 앞바퀴 순으로 닿는 느낌),
    // 감속 G에 비례해 앞바퀴 스트럿이 눌리며 기수가 숙여졌다가 롤백 때 살짝 들리는 반동을 준다.
    void SmoothLanding(WireState ws)
    {
        if (_local == null) return;
        float dt = Time.deltaTime;
        ws.landT += dt;

        // Y 보간 — 체결 직후 빠르게, 이후 서서히 수렴
        float yLerpSpeed = Mathf.Lerp(10f, 4f, Mathf.Clamp01(ws.landT / 0.6f));
        var pos = _local.transform.position;
        pos.y = Mathf.Lerp(pos.y, ws.deckTargetY, dt * yLerpSpeed);
        _local.transform.position = pos;

        // 기수 숙임 스프링: 목표 = 감속 G × 0.55° (최대 3°), 롤백 중엔 반대로 0.6° 들림
        float g      = _local.ArrestDecel / 9.81f;
        float target = Mathf.Min(g * 0.55f, 3f) - (_local.ArrestRollbackSpeed > 0.05f ? 0.6f : 0f);
        _noseDipVel += ((target - _noseDip) * 90f - _noseDipVel * 11f) * dt;
        _noseDip    += _noseDipVel * dt;

        // 회전 수평 복귀 — 피치는 기수 숙임만 남기고, 롤 → 0, 요 유지.
        // 착함 직후 기수가 들린 자세에서 앞바퀴가 내려앉는 데 약 0.4초.
        float yaw = _local.transform.eulerAngles.y;
        Quaternion level = Quaternion.Euler(_noseDip, yaw, 0f);
        _local.transform.rotation = Quaternion.Slerp(
            _local.transform.rotation, level,
            1f - Mathf.Exp(-dt * Mathf.Lerp(5f, 14f, Mathf.Clamp01(ws.landT / 0.5f)))
        );
    }

    // ── 에스코트 체결 비주얼 ──────────────────────────────────────────────────
    // 에스코트는 EscortAI가 스스로 감속하므로(와이어 존 통과 전 내부 판정으로 시작될 수도 있음),
    // BeingArrested에 들어간 에스코트가 이미 지나친 와이어 중 가장 가까운 것을 끌고 가게 한다.
    void UpdateEscortEngagements()
    {
        // 감속이 끝난 에스코트는 케이블을 놓는다
        foreach (var ws in _wires)
        {
            if (ws == null || ws.hookOwner == null || ws.caught) continue;
            var e = ws.hookOwner.GetComponent<EscortAI>();
            if (e == null || !e.IsBeingArrested) ReleaseCable(ws);
        }

        foreach (var escort in EscortAI.AllEscorts)
        {
            if (escort == null || !escort.IsBeingArrested) continue;
            if (IsHookOwner(escort.transform)) continue;

            WireState best = null;
            float bestAlong = float.MaxValue;
            Vector3 fwd = escort.transform.forward;
            foreach (var ws in _wires)
            {
                if (ws == null || ws.caught || ws.hookOwner != null) continue;
                if (ws.cable.CurrentState != ArrestingCable.State.Rest) continue;
                float along = Vector3.Dot(escort.transform.position - ws.root.position, fwd);
                if (along > 0f && along < 60f && along < bestAlong) { bestAlong = along; best = ws; }
            }
            if (best != null) Engage(best, escort.transform);
        }
    }

    bool IsHookOwner(Transform t)
    {
        foreach (var ws in _wires) if (ws != null && ws.hookOwner == t) return true;
        return false;
    }

    // ── 디버그 존 색상 ──────────────────────────────────────────────────────────
    void SetColors(WireState ws, Color zoneColor)
    {
        if (ws.discMat != null) ws.discMat.color = zoneColor;
        if (ws.light   != null) ws.light.color   = zoneColor;
    }

    // 런타임에 SO 색상을 바꾸고 인스펙터 우클릭 → "Refresh Visual State" 호출하면
    // 현재 와이어 상태에 맞춰 색을 즉시 다시 적용.
    [ContextMenu("Refresh Visual State")]
    void RefreshVisualState()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[ArrestWire] Refresh Visual State는 Play 모드에서만 동작합니다.");
            return;
        }
        foreach (var ws in _wires)
        {
            if (ws == null) continue;
            SetColors(ws, ws.stopped ? Cfg.zoneColorStopped : ws.caught ? Cfg.zoneColorCaught : Cfg.zoneColorAvailable);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_local == null) return;
        foreach (var ws in _wires)
            if (ws != null && ws.caught && !ws.stopped) _local.EndArrest();
    }

    // 와이어 인덱스(0-based)로 월드 좌표 반환 — 에스코트 착함 목표 지정에 사용.
    // Start() 이후 _wires가 빌드되므로 런타임에서만 호출해야 한다.
    public Vector3 GetWireWorldPos(int idx)
    {
        if (_wires == null || _wires.Length == 0) return transform.position;
        var ws = _wires[Mathf.Clamp(idx, 0, _wires.Length - 1)];
        return ws?.root != null ? ws.root.TransformPoint(Vector3.zero) : transform.position;
    }

    // ── 투명 재질 생성 ─────────────────────────────────────────────────────────
    static Material MakeTransparentMaterial(Color color)
    {
        string[] candidates = {
            "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit",
            "Lightweight Render Pipeline/Lit", "HDRP/Lit", "Standard",
        };
        Shader sh = null;
        foreach (string n in candidates) { sh = Shader.Find(n); if (sh != null) break; }
        sh ??= Shader.Find("Sprites/Default");

        var mat = new Material(sh);
        mat.color = color;

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        }
        else
        {
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite",   0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        mat.renderQueue = 3000;
        return mat;
    }

    // ── 에디터 기즈모 ──────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        Vector3[] positions = { Cfg.wire1LocalPos, Cfg.wire2LocalPos, Cfg.wire3LocalPos };
        Color[] colors = {
            new Color(0.8f, 0.2f, 1f, 0.4f),
            new Color(0.9f, 0.3f, 1f, 0.4f),
            new Color(1.0f, 0.4f, 1f, 0.4f),
        };
        for (int i = 0; i < 3; i++)
        {
            var q   = transform.rotation * Quaternion.Euler(0f, Cfg.deckAngleDeg, 0f);
            var pos = transform.TransformPoint(positions[i])
                    + q * new Vector3(0f, Cfg.triggerHeight * 0.5f, 0f);
            Gizmos.color  = colors[i];
            Gizmos.matrix = Matrix4x4.TRS(pos, q, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(Cfg.wireWidth, Cfg.triggerHeight, Cfg.triggerDepth));
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(Cfg.wireWidth, Cfg.triggerHeight, Cfg.triggerDepth));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
