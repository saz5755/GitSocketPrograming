using UnityEngine;
using System.Collections.Generic;

public enum GroundAnimState { Idle = 0, Walk = 1, Run = 2, Jump = 3 }

[RequireComponent(typeof(CharacterController))]
public class GroundController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float walkSpeed  = 5f;
    [SerializeField] float runSpeed   = 9f;
    [SerializeField] float jumpForce  = 8f;
    [SerializeField] float gravity    = -20f;

    [Header("Camera Control")]
    [SerializeField] float mouseSens  = 2.5f;

    [Header("Character Rotation")]
    [SerializeField] float turnSpeed  = 720f;   // 이동 방향으로 회전하는 속도 (deg/s)

    static readonly int s_hashState = Animator.StringToHash("State");
    static readonly int s_hashSpeed = Animator.StringToHash("Speed");

    CharacterController _cc;
    Animator            _anim;
    bool                _hasStateParam;
    bool                _hasSpeedParam;

    float _cameraYaw;
    float _characterYaw;
    float _vy;
    float _currentSpeed;
    float _currentAnimSpeed;   // BlendTree Speed (0.0=Idle, 0.5=Walk, 1.0=Run)
    Vector3 _lastMoveDir;

    // ── 네트워크 송신 ────────────────────────────────────────────────────────
    float _sendTimer;
    const float SendInterval = 0.05f;

    // ── 수영 (바다에 빠졌을 때) ─────────────────────────────────────────────
    [Header("Swimming")]
    [SerializeField] float swimSpeed     = 1.2f;   // 입영 중 평영 속도 (m/s)
    [SerializeField] float swimFastSpeed = 1.9f;
    [SerializeField] float swimDepth     = 1.45f;  // 수면 아래 발 위치 (모델 키를 못 잴 때 기본값) — 머리만 물 위로

    /// <summary>바다에 빠지는 순간 (입수 위치, 입수 속도 m/s). 구조 절차가 구독한다.</summary>
    public static event System.Action<GroundController, Vector3, float> EnteredWater;
    public bool    Swimming    { get; private set; }
    /// <summary>수영 중 외부 속도 (해류 등, 월드)</summary>
    public Vector3 Drift       { get; set; }
    /// <summary>설정되면 입력 대신 이 지점으로 헤엄친다 (구명환 잡기·보트로 끌려가기)</summary>
    public Vector3? SwimTarget { get; set; }
    public float    SwimTargetSpeed { get; set; } = 1.6f;
    float _swimTime;

    public void BeginSwim()
    {
        Swimming = true;
        _swimTime = 0f;
        _vy = 0f;
        _currentSpeed = 0f;
        // 모델마다 키·자세가 달라 고정 깊이로는 몸이 드러난다 — 머리 뼈 높이를 매 프레임 따라가 머리만 물 위에 남긴다
        if (_headBone == null) _headBone = FindBone(transform, "head");
        if (_shoulderBone == null) _shoulderBone = FindBone(transform, "RightArm");
        _swimDepthActual = 0f;
        // 수영 클립이 없어 Idle 위에 절차 입영 자세를 덮는다
        if (_swimPose == null && !TryGetComponent(out _swimPose)) _swimPose = gameObject.AddComponent<SwimPose>();
        _swimPose.Active = true;
    }
    float     _swimDepthActual;
    Transform _headBone, _shoulderBone;
    SwimPose  _swimPose;

    static Transform FindBone(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    // 수면에서 발(피벗)까지 깊이: 입영 시 물은 턱~목 높이 — 머리 관절(얼굴 아래쪽)이 수면 위 3cm, 단 어깨 관절은 물 위로 나오지 않게.
    // (머리 관절 높이는 리그마다 달라 DrStrange 는 어깨와 거의 같은 높이 — 둘 중 깊은 쪽을 쓴다)
    // 뼈가 없으면 렌더러 꼭대기 기준, 그것도 없으면 기본값
    float SwimDepthTarget()
    {
        if (_headBone != null)
        {
            float headH = _headBone.position.y - transform.position.y;
            if (_shoulderBone != null) headH = Mathf.Max(headH - 0.03f, _shoulderBone.position.y - transform.position.y);
            else headH -= 0.03f;
            if (headH > 0.6f) return headH;
        }
        float top = 0f;
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r.enabled && !(r is ParticleSystemRenderer)) top = Mathf.Max(top, r.bounds.max.y - transform.position.y);
        return top > 0.6f ? Mathf.Max(1.2f, top - 0.4f) : swimDepth;
    }

    public void EndSwim()
    {
        Swimming = false;
        SwimTarget = null;
        Drift = Vector3.zero;
        if (_swimPose != null) { _swimPose.Active = false; _swimPose.Speed = 0f; }
    }

    public float CameraYaw       => _cameraYaw;
    public float CurrentSpeed    => _currentSpeed;
    public float CurrentAnimSpeed => _currentAnimSpeed;
    public float WalkSpeed       => walkSpeed;
    public float RunSpeed        => runSpeed;
    public GroundAnimState CurrentAnimState { get; private set; }

    void Awake()
    {
        _cc   = GetComponent<CharacterController>();
        _anim = GetComponentInChildren<Animator>();
        CacheAnimParams();
    }

    void CacheAnimParams()
    {
        if (_anim == null) return;
        _anim.applyRootMotion = false;
        foreach (var p in _anim.parameters)
        {
            if (p.name == "State") _hasStateParam = true;
            if (p.name == "Speed") _hasSpeedParam = true;
        }
        ForceLoopLocomotionClips(_anim);
    }

    // GLB 임포터는 Loop Time을 설정하지 않으므로, 공유 에셋을 건드리지 않고
    // 로코모션 클립 복사본에만 WrapMode.Loop를 적용한다 (Jump 제외).
    static void ForceLoopLocomotionClips(Animator anim)
    {
        var src = anim.runtimeAnimatorController;
        if (src == null) return;
        var srcClips = src.animationClips;
        bool anyNeedsLoop = false;
        foreach (var c in srcClips)
            if (!c.isLooping && !c.name.ToLower().Contains("jump")) { anyNeedsLoop = true; break; }
        if (!anyNeedsLoop) return;

        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(srcClips.Length);
        foreach (var clip in srcClips)
        {
            if (clip.isLooping || clip.name.ToLower().Contains("jump"))
                pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(clip, clip));
            else
            {
                var looped = Object.Instantiate(clip);
                looped.wrapMode = WrapMode.Loop;
                pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(clip, looped));
            }
        }
        var ov = new AnimatorOverrideController(src);
        ov.ApplyOverrides(pairs);
        anim.runtimeAnimatorController = ov;
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        _vy           = 0f;
        _currentSpeed = 0f;
    }

    public void InitYaw(float yaw)
    {
        _cameraYaw    = yaw;
        _characterYaw = yaw;
    }

    void Update()
    {
        // 카메라 수평 방향 (마우스 X) — 팝업/커서 해제 중에는 회전 차단
        if (Cursor.lockState == CursorLockMode.Locked)
            _cameraYaw += Input.GetAxis("Mouse X") * mouseSens;

        float h   = Input.GetAxisRaw("Horizontal");
        float v   = Input.GetAxisRaw("Vertical");
        bool  run  = Input.GetKey(KeyCode.LeftShift);
        bool  jump = Input.GetKeyDown(KeyCode.Space);

        // 이동 방향을 카메라 수평 기준으로 계산 (W = 카메라가 바라보는 방향)
        Quaternion camYaw = Quaternion.Euler(0f, _cameraYaw, 0f);
        Vector3 dir    = (camYaw * Vector3.right * h + camYaw * Vector3.forward * v).normalized;
        bool    moving = dir.sqrMagnitude > 0.01f;

        // 캐릭터 모델 회전: 이동 중이면 이동 방향, 정지 시 카메라 방향으로 수렴
        float targetYaw = moving
            ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg
            : _cameraYaw;
        _characterYaw = Mathf.MoveTowardsAngle(_characterYaw, targetYaw, turnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, _characterYaw, 0f);

        var ocean = OceanSurface.Instance;
        if (Swimming)
        {
            UpdateSwim(ocean, dir, moving, run);
            SendNetworkUpdate();
            return;
        }
        // 갑판 밖으로 떨어져 실제 파면 아래로 들어가면 입수
        if (ocean != null && !_cc.isGrounded && _vy < 0f)
        {
            Vector3 pos = transform.position;
            if (pos.y < ocean.SampleHeight(pos.x, pos.z) - 0.2f)
            {
                EnteredWater?.Invoke(this, pos, -_vy);
                if (Swimming) { SendNetworkUpdate(); return; }
            }
        }

        // 점프 / 중력
        if (_cc.isGrounded)
        {
            if (_vy < 0f) _vy = -2f;
            if (jump) _vy = jumpForce;
        }
        _vy += gravity * Time.deltaTime;

        // 부드러운 가속 / 감속
        float targetSpeed = moving ? (run ? runSpeed : walkSpeed) : 0f;
        float lerpRate    = moving ? 10f : 16f;
        _currentSpeed = Mathf.Lerp(_currentSpeed, targetSpeed, Time.deltaTime * lerpRate);

        if (moving) _lastMoveDir = dir;
        _cc.Move((_lastMoveDir * _currentSpeed + Vector3.up * _vy) * Time.deltaTime);

        // 애니메이션 상태: 실제 _currentSpeed 기반으로 결정해 입력-속도 간 갭으로 인한 튀는 현상 방지
        if (!_cc.isGrounded)          CurrentAnimState = GroundAnimState.Jump;
        else if (_currentSpeed < 0.1f) CurrentAnimState = GroundAnimState.Idle;
        else if (_currentAnimSpeed >= 0.5f) CurrentAnimState = GroundAnimState.Run;
        else                           CurrentAnimState = GroundAnimState.Walk;

        // BlendTree Speed: 실제 _currentSpeed 기반 연속값 (로컬 Animator + 네트워크 전송 공용)
        _currentAnimSpeed = _currentSpeed < 0.01f ? 0f
            : _currentSpeed <= walkSpeed
                ? (_currentSpeed / walkSpeed) * 0.5f
                : 0.5f + ((_currentSpeed - walkSpeed) / (runSpeed - walkSpeed)) * 0.5f;

        if (_anim != null)
        {
            if (_hasStateParam)
                _anim.SetInteger(s_hashState, (int)CurrentAnimState);
            if (_hasSpeedParam)
                _anim.SetFloat(s_hashSpeed, _currentAnimSpeed, 0.08f, Time.deltaTime);
        }

        SendNetworkUpdate();
    }

    // 입영: 머리·어깨만 물 위로 내놓고 파도를 따라 오르내리며, 해류에 떠내려간다
    void UpdateSwim(OceanSurface ocean, Vector3 inputDir, bool moving, bool fast)
    {
        float dt = Time.deltaTime;
        _swimTime += dt;
        Vector3 pos = transform.position;

        Vector3 swim = Vector3.zero;
        if (SwimTarget.HasValue)
        {
            Vector3 to = SwimTarget.Value - pos; to.y = 0f;
            if (to.sqrMagnitude > 0.04f)
            {
                swim = to.normalized * Mathf.Min(SwimTargetSpeed, to.magnitude / Mathf.Max(dt, 1e-3f));
                _characterYaw = Mathf.MoveTowardsAngle(_characterYaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 240f * dt);
                transform.rotation = Quaternion.Euler(0f, _characterYaw, 0f);
            }
        }
        else if (moving) swim = inputDir * (fast ? swimFastSpeed : swimSpeed);
        _currentSpeed = Mathf.Lerp(_currentSpeed, swim.magnitude, dt * 4f);
        if (_swimPose != null) _swimPose.Speed = _currentSpeed;

        // 입수 직후 가라앉았다 떠오르는 움직임은 목표 깊이로 부드럽게 수렴시켜 표현
        float surface = ocean != null ? ocean.SampleHeight(pos.x, pos.z) : pos.y;
        float bob = Mathf.Sin(_swimTime * 2.1f) * 0.04f;
        float depth = SwimDepthTarget();
        _swimDepthActual = _swimDepthActual <= 0f ? depth : Mathf.Lerp(_swimDepthActual, depth, dt * 3f);
        float targetY = surface - _swimDepthActual + bob;
        // 입수 직후엔 천천히 떠오르고, 그 뒤엔 파고를 그대로 탄다 (지연 추종하면 너울 오르막에서 머리가 잠기고 내리막에서 가슴이 드러난다)
        float dy = (targetY - pos.y) * (_swimTime < 1.5f ? Mathf.Min(1f, dt * 2.5f) : Mathf.Min(1f, dt * 2.5f + (_swimTime - 1.5f)));

        Vector3 delta = (swim + Drift) * dt;
        delta.y = dy;
        _cc.Move(delta);

        CurrentAnimState  = GroundAnimState.Idle;
        _currentAnimSpeed = 0f;
        if (_anim != null)
        {
            if (_hasStateParam) _anim.SetInteger(s_hashState, (int)GroundAnimState.Idle);
            if (_hasSpeedParam) _anim.SetFloat(s_hashSpeed, 0f, 0.08f, dt);
        }
    }

    void SendNetworkUpdate()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;

        _sendTimer += Time.deltaTime;
        if (_sendTimer < SendInterval) return;
        _sendTimer -= SendInterval;

        Vector3 euler = transform.eulerAngles;
        // animSpeed × 100 인코딩: 0~100 = Idle/Walk/Run 연속값, 300 = Jump.
        // 수신 측(RemoteGroundInterp)은 / 100f 로 디코딩해 BlendTree Speed에 직접 적용.
        int encodedAnim = CurrentAnimState == GroundAnimState.Jump
            ? 300
            : Mathf.RoundToInt(_currentAnimSpeed * 100f);

        // GetNextTick(): Flight localTick과 공유하는 단조 증가 카운터
        // tick > 0 이면 수신 측이 out-of-order UDP 패킷을 드롭해 _targetPos 역방향 점프(버벅임)를 방지
        sc.SendMove(
            transform.position.x, transform.position.y, transform.position.z,
            euler.x, euler.y, euler.z,
            _currentSpeed > 0.1f, false, sc.GetNextTick(),
            animState: encodedAnim);
    }
}
