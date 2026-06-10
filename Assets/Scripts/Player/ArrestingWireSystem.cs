using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 항공모함 어레스팅 와이어 시스템 (3-wire).
/// 항공모함 루트 오브젝트에 부착.
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

    // ── 상수 ───────────────────────────────────────────────────────────────────
    const int Segs = 20;

    // ── 내부 상태 ──────────────────────────────────────────────────────────────
    class WireState
    {
        public Transform    root;
        public Material     wireMat;    // 케이블 재질 (emission 으로 상태 표시)
        public Renderer     disc;
        public Material     discMat;
        public Light        light;
        public LineRenderer line;
        public Material     lineMat;

        public Vector3 apexPos;
        public Vector3 apexVel;
        public float   oscDecay;
        public float   wireWorldY;

        // swept 감지 — 프레임 스킵 방지
        public float prevLocalX;
        public float prevLocalY;
        public float prevLocalZ;
        public bool  prevPosValid;

        // 스무스 착지
        public float deckTargetY;   // 체결 시점 Raycast로 탐지한 갑판 Y

        public bool  caught;
        public bool  stopped;
        public float resetTimer;
    }

    readonly WireState[] _wires = new WireState[3];
    PlayerController     _local;
    Material             _deckMat; // Aircraft_carrier_Mesh_334 에서 가져온 갑판 재질

    // 플레이어 착함 시 에스코트 경로 설정용
    Vector3 _arrestApproachDir;
    Vector3 _arrestWirePos;

    // 와이어 상태 색은 WireSystemSO에서 가져옴 — 에디터에서 직접 튜닝 가능

    // ── 생명주기 ───────────────────────────────────────────────────────────────
    void Start()
    {
        FindDeckMaterial();
        InstallDeckCollider();

        Vector3[] pos = { Cfg.wire1LocalPos, Cfg.wire2LocalPos, Cfg.wire3LocalPos };
        for (int i = 0; i < 3; i++)
            _wires[i] = BuildWire(i + 1, pos[i]);
    }

    // ── 갑판 재질 탐색 ─────────────────────────────────────────────────────────
    void FindDeckMaterial()
    {
        var mrs = GetComponentsInChildren<MeshRenderer>(false);
        foreach (var mr in mrs)
            if (mr.gameObject.name == "Aircraft_carrier_Mesh_334")
            { _deckMat = mr.sharedMaterial; break; }
    }

    // ── 갑판 BoxCollider (ConstrainToSurface Raycast 감지용) ───────────────────
    // 갑판 본체 메시(Aircraft_carrier_Mesh_334)만 사용 — 함교(island)나
    // 와이어/존 디스크 등이 maxWorldY를 끌어올려 캐릭터가 공중에 뜨는 문제 방지.
    void InstallDeckCollider()
    {
        if (transform.Find("DeckCollider") != null) return;

        var mrs = GetComponentsInChildren<MeshRenderer>(false);
        if (mrs.Length == 0) return;

        float maxWorldY = float.MinValue;
        foreach (var r in mrs)
        {
            if (r.gameObject.name != "Aircraft_carrier_Mesh_334") continue;
            if (r.bounds.max.y > maxWorldY) maxWorldY = r.bounds.max.y;
        }

        // 갑판 메시를 못 찾으면 설치 보류 (Y=함교 높이로 떠버리는 것보다 안 만드는 게 안전)
        if (maxWorldY == float.MinValue)
        {
            Debug.LogWarning("[ArrestWire] 'Aircraft_carrier_Mesh_334' MeshRenderer not found — DeckCollider 설치 건너뜀. 항모 모델 메시 이름 확인 필요.");
            return;
        }

        float localDeckY = transform.InverseTransformPoint(new Vector3(0f, maxWorldY, 0f)).y;

        var deckGO = new GameObject("DeckCollider");
        deckGO.transform.SetParent(transform, false);

        var col  = deckGO.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, localDeckY - 0.5f, 0f);
        col.size   = new Vector3(Cfg.wireWidth * 2.5f, 1.5f, 200f);

        Debug.Log($"[ArrestWire] DeckCollider 설치 localDeckY={localDeckY:F2} (maxWorldY={maxWorldY:F2})");
    }

    // ── 와이어 한 줄 생성 ──────────────────────────────────────────────────────
    WireState BuildWire(int idx, Vector3 localPos)
    {
        var go = new GameObject($"ArrestWire_{idx}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, Cfg.deckAngleDeg, 0f);

        var ws = new WireState { root = go.transform };

        BuildWireCable(ws);     // 고급 케이블 비주얼
        BuildWireRope(ws);      // V자 LineRenderer
        BuildZoneDisc(ws);      // 보라 디스크
        BuildZoneLabel(ws, idx);

        ws.apexPos      = go.transform.TransformPoint(Vector3.zero);
        ws.wireWorldY   = ws.apexPos.y;
        ws.prevPosValid = false;

        return ws;
    }

    // ── 고급 케이블 비주얼 ─────────────────────────────────────────────────────
    // Aircraft_carrier_Mesh_334 의 재질을 기반으로 강철 케이블 외관 구성
    void BuildWireCable(WireState ws)
    {
        // 재질 준비 — SO 슬롯 우선, 미할당 시 갑판 재질 복사 또는 새 머티리얼 합성
        Material mat;
        if (Cfg.wireSteelMaterial != null)
        {
            mat = new Material(Cfg.wireSteelMaterial);   // 인스턴스 복사 (와이어별 emission 독립 제어)
        }
        else if (_deckMat != null)
        {
            mat = new Material(_deckMat);
            mat.SetFloat("_Metallic",    0.88f);
            mat.SetFloat("_Smoothness",  0.72f);
            mat.SetColor("_BaseColor",   new Color(0.60f, 0.58f, 0.55f, 1f)); // 강철 회색
        }
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(sh);
            mat.SetColor("_BaseColor",  new Color(0.58f, 0.56f, 0.53f, 1f));
            mat.SetFloat("_Metallic",   0.88f);
            mat.SetFloat("_Smoothness", 0.72f);
        }
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", Cfg.emissionAvailable);
        ws.wireMat = mat;

        // ① 갑판 홈 (트러프) — 와이어가 박힌 채널, 갑판 재질 그대로 사용
        var trough = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trough.name = "WireTrough";
        trough.transform.SetParent(ws.root, false);
        trough.transform.localPosition = new Vector3(0f, -0.025f, 0f);
        trough.transform.localScale    = new Vector3(Cfg.wireWidth + 0.80f, 0.055f, 0.32f);
        Destroy(trough.GetComponent<Collider>());
        var troughMr = trough.GetComponent<MeshRenderer>();
        troughMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        troughMr.receiveShadows    = false;
        troughMr.material = _deckMat != null ? _deckMat : mat;

        // ② 주 케이블 — 가느다란 수평 실린더 (Euler(0,0,90) → X 방향으로 눕힘)
        var cable = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cable.name = "WireCable";
        cable.transform.SetParent(ws.root, false);
        cable.transform.localPosition = new Vector3(0f, 0.035f, 0f); // 갑판 홈 위로 올림
        cable.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        // scale.y = halfLength → 총 길이 = Cfg.wireWidth, scale.x/z = 반지름 ≈ 4cm
        cable.transform.localScale = new Vector3(0.042f, Cfg.wireWidth * 0.5f, 0.042f);
        Destroy(cable.GetComponent<Collider>());
        var cableMr = cable.GetComponent<MeshRenderer>();
        cableMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cableMr.receiveShadows    = false;
        cableMr.material = mat;

        // ③ 양 끝단 볼라드 (와이어 고정 피팅) — 납작한 수직 실린더
        for (int side = -1; side <= 1; side += 2)
        {
            float xPos = side * (Cfg.wireWidth * 0.5f + 0.22f);

            var bollard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bollard.name = side < 0 ? "BollardL" : "BollardR";
            bollard.transform.SetParent(ws.root, false);
            bollard.transform.localPosition = new Vector3(xPos, 0.06f, 0f);
            bollard.transform.localRotation = Quaternion.identity;
            bollard.transform.localScale    = new Vector3(0.14f, 0.065f, 0.14f); // 납작 원통
            Destroy(bollard.GetComponent<Collider>());
            var bMr = bollard.GetComponent<MeshRenderer>();
            bMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bMr.receiveShadows    = false;
            bMr.material = mat;

            // 볼라드 위 작은 캡 링
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "BollardRing";
            ring.transform.SetParent(ws.root, false);
            ring.transform.localPosition = new Vector3(xPos, 0.12f, 0f);
            ring.transform.localRotation = Quaternion.identity;
            ring.transform.localScale    = new Vector3(0.10f, 0.025f, 0.10f);
            Destroy(ring.GetComponent<Collider>());
            var rMr = ring.GetComponent<MeshRenderer>();
            rMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rMr.receiveShadows    = false;
            rMr.material = mat;
        }
    }

    // ── V자 와이어 LineRenderer ────────────────────────────────────────────────
    void BuildWireRope(WireState ws)
    {
        if (!Cfg.showLineRenderer) return;
        var ropeGO = new GameObject("WireRope");
        ropeGO.transform.SetParent(ws.root, false);

        var lr = ropeGO.AddComponent<LineRenderer>();
        lr.positionCount     = Segs + 1;
        lr.useWorldSpace     = true;
        lr.startWidth        = 0.07f;
        lr.endWidth          = 0.07f;
        lr.numCornerVertices = 5;
        lr.alignment         = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        Material mat;
        if (Cfg.wireRopeMaterial != null)
        {
            mat = new Material(Cfg.wireRopeMaterial);   // 와이어별 색 독립 제어
        }
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");
            mat = new Material(sh);
        }
        mat.color = Cfg.lineColorAvailable;
        lr.material = mat;

        ws.line    = lr;
        ws.lineMat = mat;

        ApplyRestingLine(ws);
    }

    // ── 존 디스크 ──────────────────────────────────────────────────────────────
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

    // ── LineRenderer 위치 계산 ─────────────────────────────────────────────────
    void ApplyRestingLine(WireState ws)
    {
        if (ws.line == null) return;
        Vector3 L = ws.root.TransformPoint(new Vector3(-Cfg.wireWidth * 0.5f, 0f, 0f));
        Vector3 R = ws.root.TransformPoint(new Vector3( Cfg.wireWidth * 0.5f, 0f, 0f));
        for (int i = 0; i <= Segs; i++)
        {
            float t   = (float)i / Segs;
            Vector3 p = Vector3.Lerp(L, R, t);
            p.y -= Mathf.Sin(t * Mathf.PI) * 0.15f;
            ws.line.SetPosition(i, p);
        }
    }

    void DrawWireV(WireState ws, Vector3 apex)
    {
        if (ws.line == null) return;
        int     half = Segs / 2;
        Vector3 L    = ws.root.TransformPoint(new Vector3(-Cfg.wireWidth * 0.5f, 0f, 0f));
        Vector3 R    = ws.root.TransformPoint(new Vector3( Cfg.wireWidth * 0.5f, 0f, 0f));

        for (int i = 0; i <= half; i++)
        {
            float   t   = (float)i / half;
            Vector3 p   = Vector3.Lerp(L, apex, t);
            float   sag = t * (1f - t) * 2f;
            float   osc = ws.oscDecay * 0.5f * t
                        * Mathf.Sin(t * Mathf.PI * 5f
                            + Time.time * Mathf.Lerp(5f, 22f, ws.oscDecay));
            p.y -= sag + osc;
            ws.line.SetPosition(i, p);
        }

        int rem = Segs - half;
        for (int i = 0; i <= rem; i++)
        {
            float   t   = (float)i / rem;
            Vector3 p   = Vector3.Lerp(apex, R, t);
            float   sag = t * (1f - t) * 2f;
            float   osc = ws.oscDecay * 0.5f * (1f - t)
                        * Mathf.Sin(t * Mathf.PI * 5f
                            + Time.time * Mathf.Lerp(5f, 22f, ws.oscDecay) + Mathf.PI);
            p.y -= sag + osc;
            ws.line.SetPosition(half + i, p);
        }
    }

    void UpdateWireRope(WireState ws)
    {
        if (ws.line == null) return;

        if (!ws.caught || ws.stopped)
        {
            Vector3 restPos = ws.root.TransformPoint(Vector3.zero);
            if ((ws.apexPos - restPos).sqrMagnitude < 0.004f &&
                ws.apexVel.sqrMagnitude              < 0.01f)
            {
                ws.apexPos = restPos;
                ws.apexVel = Vector3.zero;
                ApplyRestingLine(ws);
                return;
            }
            Vector3 spring = (restPos - ws.apexPos) * 40f - ws.apexVel * 12f;
            ws.apexVel += spring * Time.deltaTime;
            ws.apexPos += ws.apexVel * Time.deltaTime;
            ws.oscDecay = Mathf.MoveTowards(ws.oscDecay, 0f, Time.deltaTime * 0.8f);
            DrawWireV(ws, ws.apexPos);
            return;
        }

        Vector3 hookTarget = _local.transform.position;
        hookTarget.y = ws.wireWorldY + 0.3f;

        Vector3 force = (hookTarget - ws.apexPos) * 60f - ws.apexVel * 10f;
        ws.apexVel += force * Time.deltaTime;
        ws.apexPos += ws.apexVel * Time.deltaTime;
        ws.oscDecay = Mathf.MoveTowards(ws.oscDecay, 0f, Time.deltaTime * 0.25f);

        DrawWireV(ws, ws.apexPos);
    }

    // ── Update ─────────────────────────────────────────────────────────────────
    void Update()
    {
        if (_local == null || !_local.isLocalPlayer)
        {
            _local = null;
            foreach (var pc in PlayerController.All)
                if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
            if (_local == null) return;
        }

        float speed = _local.CurrentSpeed;

        for (int i = 0; i < _wires.Length; i++)
        {
            var ws = _wires[i];

            UpdateWireRope(ws);

            if (ws.light != null)
            {
                float target = (ws.caught && !ws.stopped)
                    ? Mathf.Lerp(5f, 12f, Mathf.Sin(Time.time * 8f) * 0.5f + 0.5f)
                    : 3f;
                ws.light.intensity = Mathf.Lerp(ws.light.intensity, target, Time.deltaTime * 5f);
            }

            if (ws.caught)
            {
                // 체결 중 스무스 착지 처리
                if (!ws.stopped) SmoothLanding(ws);

                if (!ws.stopped && speed < Cfg.stopThreshold)
                {
                    _local.EndArrest();
                    ws.stopped    = true;
                    ws.resetTimer = Cfg.resetDelay;
                    SetColors(ws, Cfg.emissionStopped, Cfg.lineColorStopped, Cfg.zoneColorStopped);
                    // 플레이어 완전 정지 후 에스코트 순차 착함 시작 (플레이어와 동일 경로)
                    AIManager.Instance?.BeginEscortLandingFromWire(_arrestApproachDir, _arrestWirePos);
                    Debug.Log($"[ArrestWire] Wire {i + 1}: 정지 → EndArrest");
                }

                if (ws.stopped)
                {
                    ws.resetTimer -= Time.deltaTime;
                    if (ws.resetTimer <= 0f)
                    {
                        ws.caught  = false;
                        ws.stopped = false;
                        SetColors(ws, Cfg.emissionAvailable, Cfg.lineColorAvailable, Cfg.zoneColorAvailable);
                        Debug.Log($"[ArrestWire] Wire {i + 1}: 리셋");
                    }
                }
                continue;
            }

            if (speed < Cfg.minCatchSpeed)
            {
                // 속도 미달이어도 이전 위치는 갱신 (swept 감지 오작동 방지)
                UpdatePrevPos(ws, ws.root.InverseTransformPoint(_local.transform.position));
                continue;
            }

            // ── Swept 감지 — 고속 비행 시 프레임 스킵 방지 ────────────────────
            // 와이어 로컬 Z=0 평면을 전 프레임과 이번 프레임 사이에 통과했는지 검사
            Vector3 lp = ws.root.InverseTransformPoint(_local.transform.position);
            bool inZone = false;

            if (ws.prevPosValid)
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
            ws.wireWorldY   = ws.root.TransformPoint(Vector3.zero).y;
            ws.deckTargetY  = DetectDeckY(_local.transform.position, ws.wireWorldY + 0.3f, _local.transform);
            ws.apexPos      = ws.root.TransformPoint(Vector3.zero);
            ws.apexVel      = _local.transform.forward * (speed * 0.25f);
            ws.oscDecay     = 1.0f;
            ws.prevPosValid = false; // 체결 직후 스윕 오감지 방지
            SetColors(ws, Cfg.emissionCaught, Cfg.lineColorCaught, Cfg.zoneColorCaught);
            _local.BeginArrest();
            _arrestApproachDir = _local.transform.forward;
            _arrestWirePos     = ws.root.TransformPoint(Vector3.zero);
            Debug.Log($"[ArrestWire] Wire {i + 1} 체결! 속도={speed:F1} m/s  deckY={ws.deckTargetY:F2}");
        }

        for (int i = 0; i < _wires.Length; i++)
        {
            var ws = _wires[i];
            // ── 어레스팅 와이어 에스코트 체결 감지 ──────────────────────────
            if (!ws.caught) CheckEscortWire(ws, i);
        }
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
        int count = Physics.RaycastNonAlloc(
            fromPos + Vector3.up * 3f, Vector3.down,
            _deckHits, 8f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        var skip = ignoreSelf ?? _local?.transform;
        float bestY = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            if (skip != null && _deckHits[k].transform.IsChildOf(skip)) continue;
            if (_deckHits[k].point.y > bestY) bestY = _deckHits[k].point.y;
        }
        return bestY > float.MinValue ? bestY + 0.3f : fallbackY;
    }

    void CheckEscortWire(WireState ws, int wireIdx)
    {
        foreach (var escort in EscortAI.AllEscorts)
        {
            if (escort == null || !escort.IsInApproachRun) continue;

            Vector3 lp = ws.root.InverseTransformPoint(escort.transform.position);

            // 에스코트 어프로치 속도(25 m/s)는 낮아 매 프레임 감지로 충분.
            // ws.prevLocal* 은 플레이어 위치이므로 에스코트 swept 감지에 사용하면 오감지 발생.
            bool inZone = Mathf.Abs(lp.x) < Cfg.wireWidth * 0.5f &&
                          lp.y > -1f && lp.y < Cfg.triggerHeight &&
                          Mathf.Abs(lp.z) < Cfg.triggerDepth * 0.5f;

            if (!inZone) continue;

            float deckY = DetectDeckY(escort.transform.position, ws.wireWorldY + 0.3f, escort.transform);
            escort.OnWireCaught(deckY);
            Debug.Log($"[ArrestWire] Wire {wireIdx + 1} 에스코트 체결: {escort.name}");
            break;
        }
    }

    // ── 스무스 착지 ──────────────────────────────────────────────────────────
    // 체결 후 전투기 Y를 갑판에 부드럽게 닿도록 보간하고 피치/롤도 수평으로 복귀
    void SmoothLanding(WireState ws)
    {
        if (_local == null) return;

        // Y 보간 — 속도에 따라 빠르게 수렴 (초반엔 빠르게, 후반 서서히)
        float yLerpSpeed = Mathf.Lerp(4f, 10f, ws.oscDecay); // oscDecay 클수록 = 착함 직후 = 빠르게
        var pos = _local.transform.position;
        pos.y = Mathf.Lerp(pos.y, ws.deckTargetY, Time.deltaTime * yLerpSpeed);
        _local.transform.position = pos;

        // 회전 수평 복귀 — 피치(X)·롤(Z) → 0, 요(Y) 유지
        float yaw = _local.transform.eulerAngles.y;
        Quaternion level = Quaternion.Euler(0f, yaw, 0f);
        _local.transform.rotation = Quaternion.Slerp(
            _local.transform.rotation, level,
            Time.deltaTime * Mathf.Lerp(3f, 6f, ws.oscDecay)
        );
    }

    // ── 색상 / 발광 일괄 적용 ───────────────────────────────────────────────────
    void SetColors(WireState ws, Color emission, Color lineColor, Color zoneColor)
    {
        if (ws.wireMat != null)
            ws.wireMat.SetColor("_EmissionColor", emission);

        if (ws.discMat != null) ws.discMat.color = zoneColor;
        if (ws.light   != null) ws.light.color    = zoneColor;
        if (ws.lineMat != null) ws.lineMat.color  = lineColor;
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
            Color emission, line, zone;
            if (ws.stopped)      { emission = Cfg.emissionStopped;   line = Cfg.lineColorStopped;   zone = Cfg.zoneColorStopped; }
            else if (ws.caught)  { emission = Cfg.emissionCaught;    line = Cfg.lineColorCaught;    zone = Cfg.zoneColorCaught; }
            else                 { emission = Cfg.emissionAvailable; line = Cfg.lineColorAvailable; zone = Cfg.zoneColorAvailable; }
            SetColors(ws, emission, line, zone);
        }
    }

    void OnDestroy()
    {
        if (_local == null) return;
        foreach (var ws in _wires)
            if (ws.caught && !ws.stopped) _local.EndArrest();
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
