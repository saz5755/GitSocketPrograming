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
