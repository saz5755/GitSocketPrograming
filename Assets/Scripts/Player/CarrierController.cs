using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 항공모함 컨트롤러.
/// [ExecuteAlways] — Play Mode 없이 씬에서 실시간 편집 가능.
/// Inspector 값 변경 시 자동 재빌드. 우클릭 → "Rebuild Carrier" 수동 재빌드.
///
/// 로컬 좌표계: Z+ = 함수(Bow), X+ = 우현(Starboard), X- = 좌현(Port)
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Rigidbody))]
public class CarrierController : MonoBehaviour
{
    [Header("항해")]
    [SerializeField] bool  enableMovement  = false;
    [SerializeField] float speedKnots     = 28f;
    [SerializeField] float headingDegrees = 0f;

    [Header("갑판 레이아웃")]
    [SerializeField] Vector3 deckCenter        = new Vector3(  0f, 5.0f,   0f);
    [SerializeField] Vector3 deckSize          = new Vector3( 50f, 1.0f, 240f);
    [SerializeField] Vector3 landingZoneLocal  = new Vector3(-18f, 5.5f, -55f);
    [SerializeField] Vector3 takeoffZoneLocal  = new Vector3(-20f, 5.5f,  95f);
    [SerializeField] float   landingZoneRadius = 28f;
    [SerializeField] float   takeoffZoneRadius = 18f;

    [Header("FLOLS")]
    [SerializeField] float   glideslopeAngle = 3.5f;
    [SerializeField] float   flolsRange      = 2000f;
    [SerializeField] Vector3 flolsMountLocal = new Vector3(-22f, 7f, -30f);

    [Header("카타펄트")]
    [SerializeField] float catapultSpeedMS = 72f;

    [Header("에스코트 존")]
    [SerializeField] float escortZoneRadius = 800f;
    public float EscortZoneRadius => escortZoneRadius;

    [Header("에스코트 내측 회피 존 (항모 주변 진입 금지 반경)")]
    [SerializeField] float innerAvoidanceRadius = 350f;
    public float InnerAvoidanceRadius => innerAvoidanceRadius;

    // ── 내부 참조 ─────────────────────────────────────────────────────────────
    Rigidbody    _rb;
    AircraftZone _landingZone;
    AircraftZone _takeoffZone;
    ILSBeacon    _ils;

    Light _flolsHigh;
    Light _flolsCenter;
    Light _flolsLow;

    float _speedMS;

    public ILSBeacon ILS           => _ils;
    public float     CatapultSpeed => catapultSpeedMS;

    // ── 생명주기 ──────────────────────────────────────────────────────────────
    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.isKinematic   = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
        _speedMS = speedKnots * 0.5144f;
        transform.rotation = Quaternion.Euler(0f, headingDegrees, 0f);
    }

    void OnEnable() => ClearAndBuild();

    // Inspector 값 변경 시 에디터에서 즉시 재빌드
    void OnValidate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorApplication.delayCall += () => { if (this != null) ClearAndBuild(); };
#endif
    }

    void FixedUpdate()
    {
        if (!Application.isPlaying || !enableMovement) return;
        _rb.MovePosition(transform.position + transform.forward * _speedMS * Time.fixedDeltaTime);
    }

    void Update()
    {
        if (Application.isPlaying) UpdateFLOLS();
    }

    // ── 수동 재빌드 (Inspector 우클릭 메뉴) ──────────────────────────────────
    [ContextMenu("Rebuild Carrier")]
    void ClearAndBuild()
    {
        ClearChildren();

        _landingZone = null;
        _takeoffZone = null;
        _ils         = null;
        _flolsHigh = _flolsCenter = _flolsLow = null;

        _speedMS = speedKnots * 0.5144f;
        transform.rotation = Quaternion.Euler(0f, headingDegrees, 0f);

        BuildCarrier();
        BuildFLOLS();
        BuildILSBeacon();
    }

    // 자식 오브젝트 전체 제거 (에디터: DestroyImmediate, 런타임: Destroy)
    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(child); continue; }
#endif
            Destroy(child);
        }
    }

    // ── 항모 전체 형상 ─────────────────────────────────────────────────────────
    void BuildCarrier()
    {
        BuildHull();
        BuildMainDeck();
        BuildAngledDeck();
        BuildIsland();
        BuildRunwayMarkings();
        BuildZones();
        BuildLaunchOfficer();
    }

    void BuildHull()
    {
        MakePart("Hull_Main",  new Vector3( 0f, 0f,    0f), new Vector3(36f, 10f, 220f), new Color(0.13f, 0.13f, 0.15f));
        MakePart("Hull_Bow",   new Vector3(-2f, 0f,  116f), new Vector3(30f,  9f,  14f), new Color(0.12f, 0.12f, 0.14f));
        MakePart("Hull_Stern", new Vector3( 0f, 0f, -116f), new Vector3(34f,  9f,  12f), new Color(0.12f, 0.12f, 0.14f));
    }

    void BuildMainDeck()
    {
        MakePart("Deck_Main", deckCenter, deckSize, new Color(0.22f, 0.22f, 0.24f));
    }

    void BuildAngledDeck()
    {
        var go = MakePart("Deck_Angled",
            new Vector3(-20f, deckCenter.y - 0.05f, -28f),
            new Vector3(32f, 1f, 135f),
            new Color(0.21f, 0.21f, 0.23f));
        go.transform.localRotation = Quaternion.Euler(0f, -9f, 0f);
    }

    void BuildIsland()
    {
        float sx    = 22f;
        float baseY = deckCenter.y + 0.5f;

        MakePart("Island_Base",  new Vector3(sx,      baseY +  7.5f, 35f), new Vector3(10f, 15f, 36f), new Color(0.37f, 0.37f, 0.39f));
        MakePart("Island_Upper", new Vector3(sx,      baseY + 17.0f, 38f), new Vector3( 8f,  6f, 26f), new Color(0.31f, 0.31f, 0.33f));
        MakePart("Island_Mast",  new Vector3(sx,      baseY + 27.0f, 38f), new Vector3( 2f, 11f,  2f), new Color(0.25f, 0.25f, 0.27f));
        MakePart("Radar_Boom",   new Vector3(sx,      baseY + 33.0f, 38f), new Vector3( 9f,  1f,  4f), new Color(0.22f, 0.22f, 0.24f));
        MakePart("Funnel_A",     new Vector3(sx - 1f, baseY + 17.0f, 16f), new Vector3( 6f,  9f,  8f), new Color(0.28f, 0.28f, 0.30f));
        MakePart("Funnel_B",     new Vector3(sx - 1f, baseY + 14.0f,  4f), new Vector3( 5f,  6f,  7f), new Color(0.28f, 0.28f, 0.30f));
    }

    void BuildRunwayMarkings()
    {
        float my = deckCenter.y + 0.51f;

        // 카타펄트 트랙 2줄 (좌현)
        for (int lane = 0; lane < 2; lane++)
        {
            float lx = -15f - lane * 3.5f;
            MakeMark($"Cat_{lane}_Track", new Vector3(lx, my, 35f), new Vector3(0.7f, 0.02f, 115f), new Color(0.70f, 0.65f, 0.45f));
        }

        // 사선갑판 착함 센터라인 (−9°)
        for (int i = -3; i <= 3; i++)
        {
            var m = MakeMark($"LandLine_{i}",
                new Vector3(-15f, my, -22f + i * 18f),
                new Vector3(1.3f, 0.02f, 13f),
                new Color(0.9f, 0.9f, 0.9f));
            m.transform.localRotation = Quaternion.Euler(0f, -9f, 0f);
        }

        // 어레스팅 와이어 3줄 (황색, −9°)
        float[] wireZ = { -58f, -64f, -70f };
        foreach (float wz in wireZ)
        {
            var w = MakeMark("ArrWire", new Vector3(-10f, my, wz), new Vector3(26f, 0.05f, 0.8f), new Color(1f, 0.85f, 0f));
            w.transform.localRotation = Quaternion.Euler(0f, -9f, 0f);
        }

        // 갑판 가장자리 흰선
        MakeMark("Edge_Port",  new Vector3(-24f, my,    0f), new Vector3(0.4f, 0.02f, 235f), Color.white);
        MakeMark("Edge_Stbd",  new Vector3( 24f, my,    0f), new Vector3(0.4f, 0.02f, 235f), Color.white);
        MakeMark("Edge_Bow",   new Vector3(  0f, my,  117f), new Vector3( 48f, 0.02f,  0.4f), Color.white);
        MakeMark("Edge_Stern", new Vector3(  0f, my, -117f), new Vector3( 48f, 0.02f,  0.4f), Color.white);
    }

    void BuildZones()
    {
        var lzGO = new GameObject("CarrierLandingZone");
        lzGO.transform.SetParent(transform, false);
        lzGO.transform.localPosition = landingZoneLocal;
        _landingZone = lzGO.AddComponent<AircraftZone>();
        _landingZone.Configure(AircraftZone.Type.Carrier, landingZoneRadius);

        var tzGO = new GameObject("CarrierTakeoffZone");
        tzGO.transform.SetParent(transform, false);
        tzGO.transform.localPosition = takeoffZoneLocal;
        _takeoffZone = tzGO.AddComponent<AircraftZone>();
        _takeoffZone.Configure(AircraftZone.Type.Takeoff, takeoffZoneRadius);

        // 에스코트 외부 존 — 이 반경 내부에서 자유비행
        var ezGO = new GameObject("EscortZone");
        ezGO.transform.SetParent(transform, false);
        var ezCol = ezGO.AddComponent<SphereCollider>();
        ezCol.isTrigger = true;
        ezCol.radius = escortZoneRadius;
        if (Application.isPlaying)
            ezGO.AddComponent<EscortZoneTrigger>();

        // 에스코트 내측 회피 존 — 이 반경 내부로는 자유비행 중 진입하지 않음
        if (Application.isPlaying)
        {
            var izGO = new GameObject("EscortInnerZone");
            izGO.transform.SetParent(transform, false);
            var izCol = izGO.AddComponent<SphereCollider>();
            izCol.isTrigger = true;
            izCol.radius = innerAvoidanceRadius;
            izGO.AddComponent<EscortInnerZoneTrigger>();
        }
    }

    // 카타펄트 오피서 NPC — 탑승 존 앞쪽, 항모 진행 방향을 바라보며 대기
    void BuildLaunchOfficer()
    {
        if (!Application.isPlaying) return;

        float deckY = deckCenter.y + 0.05f;

        // 탑승 존(Z=95) 앞에 배치, 카타펄트 트랙(X≈-15) 오른쪽에 서서 후방(−Z)을 바라봄
        var go = new GameObject("LaunchOfficer");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(-10f, deckY, 108f);
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // 후방(항공기 방향)을 향함

        go.AddComponent<LaunchOfficerNPC>();
    }

    // ── FLOLS ─────────────────────────────────────────────────────────────────
    void BuildFLOLS()
    {
        var mount = new GameObject("FLOLS_Mount");
        mount.transform.SetParent(transform, false);
        mount.transform.localPosition = flolsMountLocal;

        _flolsHigh   = MakeFLOLSLight(mount.transform, new Vector3(0f,  0.9f, 0f), Color.red,              "FLOLS_Hi");
        _flolsCenter = MakeFLOLSLight(mount.transform, new Vector3(0f,  0f,   0f), new Color(1f,0.75f,0f), "FLOLS_On");
        _flolsLow    = MakeFLOLSLight(mount.transform, new Vector3(0f, -0.9f, 0f), Color.red,              "FLOLS_Lo");
    }

    Light MakeFLOLSLight(Transform parent, Vector3 localPos, Color color, string objName)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(-glideslopeAngle, 180f, 0f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.color = color;
        l.spotAngle = 8f; l.innerSpotAngle = 4f;
        l.range = flolsRange; l.intensity = 0f;
        return l;
    }

    void BuildILSBeacon()
    {
        if (_landingZone == null) return;
        var go = new GameObject("ILSBeacon");
        go.transform.SetParent(_landingZone.transform, false);
        _ils = go.AddComponent<ILSBeacon>();
        _ils.Configure(glideslopeAngle, transform);
    }

    void UpdateFLOLS()
    {
        if (_landingZone == null) { SetFLOLS(0f, 0f, 0f); return; }

        var gm = GameModeManager.Instance;
        if (gm == null || !gm.IsFlying || gm.LocalAircraft == null)
        { SetFLOLS(0f, 0f, 0f); return; }

        Vector3 pos  = gm.LocalAircraft.position;
        float   dist = Vector3.Distance(pos, _landingZone.transform.position);

        if (dist > flolsRange || dist < 80f) { SetFLOLS(0f, 0f, 0f); return; }

        float vDev = _ils != null ? _ils.VerticalDeviation(pos) : 0f;
        float fade = Mathf.Clamp01(1f - dist / flolsRange) * 10f;

        if      (vDev >  8f) SetFLOLS(fade, 0f,   0f);
        else if (vDev < -8f) SetFLOLS(0f,   0f,   fade);
        else                 SetFLOLS(0f,   fade, 0f);
    }

    void SetFLOLS(float high, float center, float low)
    {
        if (_flolsHigh   != null) _flolsHigh.intensity   = high;
        if (_flolsCenter != null) _flolsCenter.intensity = center;
        if (_flolsLow    != null) _flolsLow.intensity    = low;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────
    GameObject MakePart(string partName, Vector3 localPos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = partName;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = MakeMaterial(color);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    GameObject MakeMark(string markName, Vector3 localPos, Vector3 size, Color color)
    {
        var go  = MakePart(markName, localPos, size, color);
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(col);
            else
#endif
            Destroy(col);
        }
        return go;
    }

    static Material MakeMaterial(Color color)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        return new Material(sh) { color = color };
    }
}
