using System.Collections.Generic;
using UnityEngine;

public class FlightCamera : MonoBehaviour
{
    [Header("Chase Camera")]
    [SerializeField] Vector3 chaseOffset = new Vector3(0f, 3f, -12f);
    [SerializeField] float chaseSmooth = 6f;

    [Header("Cockpit Camera")]
    [SerializeField] Vector3 cockpitOffset    = new Vector3(0f, 1.75f, 2.45f);
    [SerializeField] float   cockpitDownTilt  = 8f;    // 기본 하향 시선 각도 (도)
    [SerializeField] float   cockpitFOV       = 55f;
    [SerializeField] float   chaseFOV         = 60f;

    [Header("Free Look (우클릭 드래그)")]
    [SerializeField] float freeLookSensitivity = 3f;
    [SerializeField] float freeLookReturnSpeed = 8f;
    [SerializeField] float freeLookMaxPitch    = 80f;


    bool isCockpit = false;
    public bool IsCockpit => isCockpit;
    public Vector3 CockpitOffset => cockpitOffset;   // 조종사 눈 위치 (FlightHUD 가 HUD 유리를 시선 앞에 배치할 때 사용)
    public float   CockpitDownTilt => cockpitDownTilt;
    PlayerController localPlayer;
    MFDController    mfdController;
    FlightHUD        _flightHUD;

    public Transform CockpitObj10 { get; private set; }

    // KF-21 모델 오브젝트 이름으로 렌더러 분류
    // 코크핏 모드에서만 표시 (조종석 내부)
    static readonly HashSet<string> CockpitInteriorNames = new HashSet<string>
    {
        "Object_4",                                              // 코크핏 테두리/프레임 (바닥·측면 채움)
        "Object_10", "Object_12", "Object_13", "Object_14",
        "Object_15", "Object_16", "Object_20", "Object_21"
    };
    // 코크핏·추격 양쪽에서 항상 표시 (날개·주 동체 — 조종석에서 옆을 보면 날개가 보임)
    static readonly HashSet<string> AlwaysVisibleNames = new HashSet<string>
    {
        "Object_2",  // 주 동체 + 날개
    };
    const string CanopyObjectName = "Object_6";

    Renderer[] _exteriorRenderers; // 추격 카메라에서만 표시 (코크핏 시 숨김)
    Renderer[] _cockpitRenderers;  // 코크핏 모드에서만 표시
    Renderer[] _alwaysRenderers;   // 항상 표시 — 날개가 양쪽에서 보여야 하므로
    Renderer _canopyRenderer;  // 항상 표시 유지용 참조

    float freeLookYaw   = 0f;
    float freeLookPitch = 0f;

    float _shakeMag, _shakeTimer, _shakeDur;

    // ── 표적 카메라 (홀드) / 미사일 캠 (v4.2 C-3, C-4) ─────────────────────
    static Transform s_lastLocalMissile;
    /// <summary>로컬 플레이어가 미사일을 쏠 때 MissileLauncher 가 알린다 — 미사일 캠 대상</summary>
    public static void NotifyLocalMissile(Transform missile) => s_lastLocalMissile = missile;

    TargetingSystem _targeting;
    float     _tgtBlend;          // 0..1 표적 카메라 전환량
    Transform _missileCamTarget;
    Vector3   _missileLastPos, _missileLastFwd;
    float     _missileLinger = -1f;   // 미사일 소멸 후 폭발 지점 응시 남은 시간
    bool      _missileCamWasCockpit;
    const float MissileLingerSec = 1.4f;
    bool      _missileTracking;   // 미사일을 따라가는 중 (Destroy 된 뒤에도 한 프레임 판별용)
    public bool IsMissileCam => _missileTracking || _missileLinger > 0f;

    // ── 지상 모드 ──────────────────────────────────────────────────────────
    Transform        _groundTarget;
    GroundController _groundController;
    float            _groundPitch = 10f;

    // ── 탑승(보딩) 모드 ────────────────────────────────────────────────────
    bool      _isBoardingMode  = false;
    Transform _boardingAircraft;

    [Header("Ground Camera")]
    [SerializeField] Vector3 groundOffset = new Vector3(0f, 2.5f, -5f);
    [SerializeField] float   groundSmooth = 8f;

    public void TriggerShake(float magnitude, float duration)
    {
        _shakeMag   = magnitude;
        _shakeTimer = duration;
        _shakeDur   = duration;
    }

    void Awake()
    {
        mfdController  = gameObject.AddComponent<MFDController>();
    }

    void Start()
    {
        FindLocalPlayer();
    }

    void FindLocalPlayer()
    {
        foreach (var pc in PlayerController.All)
        {
            if (pc != null && pc.isLocalPlayer)
            {
                localPlayer = pc;
                BuildRendererLists(pc);
                break;
            }
        }
    }

    void BuildRendererLists(PlayerController pc)
    {
        mfdController?.Initialize(pc);
        var all = pc.GetComponentsInChildren<Renderer>(true);
        var exterior  = new List<Renderer>();
        var interior  = new List<Renderer>();
        var alwaysVis = new List<Renderer>();
        _canopyRenderer = null;

        foreach (var r in all)
        {
            string n = r.gameObject.name;
            if (n == CanopyObjectName)
            {
                _canopyRenderer = r;
            }
            else if (AlwaysVisibleNames.Contains(n))
            {
                alwaysVis.Add(r);
            }
            else if (CockpitInteriorNames.Contains(n))
            {
                interior.Add(r);
                if (n == "Object_10") CockpitObj10 = r.transform;
            }
            else
            {
                exterior.Add(r);
            }
        }

        _exteriorRenderers = exterior.ToArray();
        _cockpitRenderers  = interior.ToArray();
        _alwaysRenderers   = alwaysVis.ToArray();

        // KF21 메시 전체 활성 보장 — 외부·캐노피는 항상 켜두고, 코크핏 내부만 모드에 따라 토글
        foreach (var r in _exteriorRenderers) r.enabled = true;
        foreach (var r in _alwaysRenderers)   r.enabled = true;
        if (_canopyRenderer != null)          _canopyRenderer.enabled = true;
        // 코크핏 내부는 현재 isCockpit 상태에 맞춰 초기화
        foreach (var r in _cockpitRenderers)  r.enabled = isCockpit;
    }

    // ── 외부 API ────────────────────────────────────────────────────────────
    public void SetGroundTarget(Transform t)
    {
        _deathFocus       = null;
        _isBoardingMode   = false;
        _boardingAircraft = null;
        for (int i = 0; i < 3; i++) { var sw = CockpitSwitch.Get(i); sw?.SetVisible(false); sw?.Unregister(); }
        _groundTarget     = t;
        _groundController = t != null ? t.GetComponent<GroundController>() : null;
        _groundPitch      = 10f;
        if (isCockpit) SetCockpit(false);
    }

    // 콕핏 탑승 모드 진입 — 항모 갑판에서 탑승, HUD 없이 1인칭 조종석 시점
    public void BeginCockpitBoarding(PlayerController pc)
    {
        if (pc == null) return;
        _groundTarget     = null;
        _boardingAircraft = pc.transform;
        _isBoardingMode   = true;
        if (_cockpitRenderers == null || _cockpitRenderers.Length == 0)
            BuildRendererLists(pc);
        SetCockpit(true, false);   // 조종석 모델 표시, HUD 미활성

        // 이 항공기의 CockpitSwitch를 레지스트리에 등록 후 표시
        // (원격 플레이어 항공기의 동일 컴포넌트를 덮어쓰지 않도록 Awake 등록 대신 여기서만 등록)
        foreach (var sw in pc.GetComponentsInChildren<CockpitSwitch>(true))
        {
            sw.Register();
            sw.SetVisible(true);
            sw.SetState(CockpitSwitch.State.Locked);
        }
    }

    // 콕핏 탑승 모드 종료 — ESC 취소 또는 출격 직전 정리
    public void EndCockpitBoarding()
    {
        for (int i = 0; i < 3; i++)
        {
            var sw = CockpitSwitch.Get(i);
            if (sw == null) continue;
            sw.SetVisible(false);
            sw.Unregister();
        }
        _isBoardingMode   = false;
        _boardingAircraft = null;
        SetCockpit(false, false);
    }

    // ── 사망 카메라 — 외부 시점으로 잔해를 천천히 선회하며 추적 (추락 지점에 닿으면 그 자리 유지) ──
    Transform _deathFocus;
    Vector3   _deathFocusPos;
    float     _deathOrbitYaw;

    public void BeginDeathCam(Transform wreck)
    {
        if (IsMissileCam) EndMissileCam();
        _tgtBlend = 0f;
        _deathFocus    = wreck;
        _deathFocusPos = wreck.position;
        _deathOrbitYaw = wreck.eulerAngles.y + 150f;
        if (isCockpit) SetCockpit(false);
    }

    void UpdateDeathCam()
    {
        if (_deathFocus != null && _deathFocus.gameObject.activeInHierarchy)
            _deathFocusPos = Vector3.Lerp(_deathFocusPos, _deathFocus.position, 1f - Mathf.Exp(-8f * Time.deltaTime));
        _deathOrbitYaw += 12f * Time.deltaTime;
        Vector3 offset = Quaternion.Euler(18f, _deathOrbitYaw, 0f) * new Vector3(0f, 0f, -55f);
        Vector3 pos = _deathFocusPos + offset;
        transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-3f * Time.deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(_deathFocusPos - transform.position), 1f - Mathf.Exp(-6f * Time.deltaTime));
    }

    public void SetFlightTarget(PlayerController pc)
    {
        _deathFocus   = null;
        _missileTracking = false; _missileLinger = -1f;
        _groundTarget = null;
        localPlayer   = pc;
        BuildRendererLists(pc);
    }

    void LateUpdate()
    {
        if (_deathFocus != null)
        {
            UpdateDeathCam();
            return;
        }

        if (_groundTarget != null)
        {
            UpdateGround();
            return;
        }

        if (_isBoardingMode)
        {
            UpdateBoardingCockpit();
            return;
        }

        if (localPlayer == null)
        {
            FindLocalPlayer();
            return;
        }

        if (UpdateMissileCam()) return;

        if (SimInput.Pressed(SimAction.ViewToggle))
            SetCockpit(!isCockpit);

        bool freeLook = SimInput.FreeLook;
        if (freeLook)
        {
            freeLookYaw   += Input.GetAxis("Mouse X") * freeLookSensitivity;
            freeLookPitch -= Input.GetAxis("Mouse Y") * freeLookSensitivity;
            freeLookPitch  = Mathf.Clamp(freeLookPitch, -freeLookMaxPitch, freeLookMaxPitch);
        }
        else
        {
            freeLookYaw   = Mathf.Lerp(freeLookYaw,   0f, freeLookReturnSpeed * Time.deltaTime);
            freeLookPitch = Mathf.Lerp(freeLookPitch, 0f, freeLookReturnSpeed * Time.deltaTime);
        }

        if (isCockpit)
            UpdateCockpit();
        else
            UpdateChase();
        ApplyTargetCam();

        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            float decay = _shakeDur > 0f ? _shakeTimer / _shakeDur : 0f;
            transform.position += Random.insideUnitSphere * (_shakeMag * decay);
        }
    }

    // ── 탑승 모드: 주기된 항공기 콕핏 1인칭 ────────────────────────────────
    void UpdateBoardingCockpit()
    {
        if (_boardingAircraft == null) return;
        bool rmb = Input.GetMouseButton(1);
        if (rmb)
        {
            freeLookYaw   += Input.GetAxis("Mouse X") * freeLookSensitivity;
            freeLookPitch -= Input.GetAxis("Mouse Y") * freeLookSensitivity;
            freeLookPitch  = Mathf.Clamp(freeLookPitch, -freeLookMaxPitch, freeLookMaxPitch);
        }
        else
        {
            freeLookYaw   = Mathf.Lerp(freeLookYaw,   0f, freeLookReturnSpeed * Time.deltaTime);
            freeLookPitch = Mathf.Lerp(freeLookPitch, 0f, freeLookReturnSpeed * Time.deltaTime);
        }
        Transform  t        = _boardingAircraft;
        transform.position  = t.TransformPoint(cockpitOffset);
        Quaternion baseTilt = t.rotation * Quaternion.Euler(cockpitDownTilt, 0f, 0f);
        transform.rotation  = baseTilt * Quaternion.Euler(freeLookPitch, freeLookYaw, 0f);
    }

    // ── 지상 3인칭 카메라 ──────────────────────────────────────────────────
    void UpdateGround()
    {
        if (_groundTarget == null) return;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            _groundPitch -= Input.GetAxis("Mouse Y") * freeLookSensitivity;
            _groundPitch  = Mathf.Clamp(_groundPitch, -15f, 55f);
        }

        float cameraYaw = _groundController != null
            ? _groundController.CameraYaw
            : _groundTarget.eulerAngles.y;
        Quaternion camRot = Quaternion.Euler(_groundPitch, cameraYaw, 0f);

        Vector3 targetPos  = _groundTarget.position + camRot * groundOffset;
        // CharacterController 캡슐(height=1.8m) 상단 + SphereCast radius(0.3m) 위에서 시작
        // → 자신의 캡슐을 절대 self-hit하지 않음 (레이어 제외 없이 안전)
        Vector3 castOrigin = _groundTarget.position + Vector3.up * 2.1f;
        Vector3 castDir    = targetPos - castOrigin;
        if (Physics.SphereCast(castOrigin, 0.3f, castDir.normalized, out RaycastHit hit,
                               castDir.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            targetPos = hit.point + hit.normal * 0.3f;
        }

        transform.position = Vector3.Lerp(transform.position, targetPos, groundSmooth * Time.deltaTime);

        Vector3 lookAt = _groundTarget.position + Vector3.up * 1.2f;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(lookAt - transform.position),
            groundSmooth * Time.deltaTime);
    }

    void UpdateCockpit()
    {
        Transform t = localPlayer.transform;
        transform.position = t.TransformPoint(cockpitOffset);
        // 기본 하향 시선 + 자유시점 오프셋
        Quaternion downTilt     = Quaternion.Euler(cockpitDownTilt, 0f, 0f);
        Quaternion freeLookOff  = Quaternion.Euler(freeLookPitch, freeLookYaw, 0f);
        transform.rotation = t.rotation * downTilt * freeLookOff;
    }

    void UpdateChase()
    {
        Transform t = localPlayer.transform;
        Vector3 target = t.TransformPoint(chaseOffset);
        transform.position = Vector3.Lerp(transform.position, target, chaseSmooth * Time.deltaTime);

        Quaternion freeLookOffset = Quaternion.Euler(freeLookPitch, freeLookYaw, 0f);
        Quaternion targetRot = t.rotation * freeLookOffset;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, chaseSmooth * Time.deltaTime);
    }

    // ── 표적 카메라: 홀드하는 동안 락온 표적을 화면에 담는다 (AC7 TGT) ──────────
    // 추격 시점은 내 기체 뒤-위에서 표적 쪽을 바라보고, 콕핏 시점은 조종사가 고개를 돌려 표적을 본다(패드락).
    void ApplyTargetCam()
    {
        if (_targeting == null || _targeting.LocalPlayer != localPlayer)
        {
            _targeting = null;
            foreach (var ts in TargetingSystem.All)
                if (ts != null && ts.LocalPlayer == localPlayer) { _targeting = ts; break; }
        }
        var target = _targeting != null && _targeting.State != TargetingSystem.LockState.None ? _targeting.Target : null;
        bool want = target != null && SimInput.Held(SimAction.TargetCam) && !SimInput.FreeLook;
        _tgtBlend = Mathf.MoveTowards(_tgtBlend, want ? 1f : 0f, Time.deltaTime * 3.5f);
        if (_tgtBlend <= 0f || target == null) return;

        float w = Mathf.SmoothStep(0f, 1f, _tgtBlend);
        Transform t = localPlayer.transform;
        Vector3 toTgt = target.transform.position - t.position;
        if (toTgt.sqrMagnitude < 1f) return;

        if (isCockpit)
        {
            Quaternion look = Quaternion.LookRotation(toTgt.normalized, t.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, w);
        }
        else
        {
            Vector3 dir = toTgt.normalized;
            Vector3 pos = t.position - dir * 16f + t.up * 4.5f;
            Vector3 focus = Vector3.Lerp(t.position, target.transform.position, 0.5f);
            if ((focus - t.position).sqrMagnitude > 250f * 250f) focus = t.position + dir * 250f;
            transform.position = Vector3.Lerp(transform.position, pos, w);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(focus - transform.position, t.up), w);
        }
    }

    // ── 미사일 캠: 발사 후 키를 누르면 마지막 미사일을 따라가고, 명중/소멸 지점을 잠시 비춘 뒤 복귀 ──
    // 멀티플레이라 시간 배속(슬로모션)은 쓰지 않는다 — 폭발 지점에 카메라를 머무르게 해 대신한다.
    bool UpdateMissileCam()
    {
        if (SimInput.Pressed(SimAction.MissileCam))
        {
            if (IsMissileCam) EndMissileCam();
            else if (s_lastLocalMissile != null)
            {
                _missileCamTarget     = s_lastLocalMissile;
                _missileTracking      = true;
                _missileLinger        = -1f;
                _missileCamWasCockpit = isCockpit;
                if (isCockpit) SetCockpit(false);
            }
        }
        if (!IsMissileCam) return false;

        if (_missileTracking && (_missileCamTarget == null || !_missileCamTarget.gameObject.activeInHierarchy))
        {
            _missileTracking  = false;
            _missileCamTarget = null;
            _missileLinger    = MissileLingerSec;
            TriggerShake(0.25f, 0.4f);
        }

        if (_missileTracking)
        {
            _missileLastPos = _missileCamTarget.position;
            _missileLastFwd = _missileCamTarget.forward;
            Vector3 pos = _missileLastPos - _missileLastFwd * 7f + Vector3.up * 1.4f;
            transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-12f * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(_missileLastPos + _missileLastFwd * 40f - transform.position),
                1f - Mathf.Exp(-10f * Time.deltaTime));
        }
        else
        {
            // 폭발 지점을 바라보며 살짝 물러난다
            _missileLinger -= Time.deltaTime;
            transform.position -= (_missileLastPos - transform.position).normalized * 6f * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(_missileLastPos - transform.position), 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (_missileLinger <= 0f) { EndMissileCam(); return false; }
        }

        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            float decay = _shakeDur > 0f ? _shakeTimer / _shakeDur : 0f;
            transform.position += Random.insideUnitSphere * (_shakeMag * decay);
        }
        return true;
    }

    void EndMissileCam()
    {
        _missileCamTarget = null;
        _missileTracking  = false;
        _missileLinger    = -1f;
        if (_missileCamWasCockpit) SetCockpit(true);
        _missileCamWasCockpit = false;
    }

    // withHUD=false: 탑승 모드용 — 조종석 뷰는 활성화하되 HUD·MFD는 미표시
    void SetCockpit(bool cockpit, bool withHUD = true)
    {
        isCockpit = cockpit;

        var cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.fieldOfView   = cockpit ? cockpitFOV : chaseFOV;
            cam.nearClipPlane = cockpit ? 0.02f : 0.3f;
        }

        // 코크핏 내부 오브젝트만 모드에 따라 토글 (외부 메시는 항상 활성 유지)
        if (_cockpitRenderers != null)
            foreach (var r in _cockpitRenderers) r.enabled = cockpit;

        if (_canopyRenderer != null)
            _canopyRenderer.enabled = true;

        mfdController?.SetVisible(cockpit && withHUD);

        // HUD를 캐노피 유리에 투영 (탑승 중에는 HUD 미활성)
        if (_flightHUD == null) _flightHUD = FindAnyObjectByType<FlightHUD>();
        _flightHUD?.SetCockpitGlass(cockpit && withHUD, GetComponent<Camera>());
    }

}
