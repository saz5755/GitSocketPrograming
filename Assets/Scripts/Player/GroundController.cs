using UnityEngine;

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

    CharacterController _cc;
    Animator            _anim;
    bool                _hasStateParam;
    bool                _hasSpeedParam;

    float _cameraYaw;
    float _characterYaw;
    float _vy;
    float _currentSpeed;
    Vector3 _lastMoveDir;

    public float CameraYaw    => _cameraYaw;
    public float CurrentSpeed => _currentSpeed;
    public float WalkSpeed    => walkSpeed;
    public float RunSpeed     => runSpeed;
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
        foreach (var p in _anim.parameters)
        {
            if (p.name == "State") _hasStateParam = true;
            if (p.name == "Speed") _hasSpeedParam = true;
        }
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
        // 카메라 수평 방향 (마우스 X) — 카메라 오빗 기준
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

        // 애니메이션 상태
        if (!_cc.isGrounded)  CurrentAnimState = GroundAnimState.Jump;
        else if (!moving)     CurrentAnimState = GroundAnimState.Idle;
        else if (run)         CurrentAnimState = GroundAnimState.Run;
        else                  CurrentAnimState = GroundAnimState.Walk;

        if (_anim != null)
        {
            if (_hasStateParam)
                _anim.SetInteger("State", (int)CurrentAnimState);

            if (_hasSpeedParam)
            {
                // Soldier.controller BlendTree: 0=Idle, 0.5=Walk, 1.0=Run
                float animSpeed = _currentSpeed < 0.01f ? 0f
                    : _currentSpeed <= walkSpeed
                        ? (_currentSpeed / walkSpeed) * 0.5f
                        : 0.5f + ((_currentSpeed - walkSpeed) / (runSpeed - walkSpeed)) * 0.5f;
                _anim.SetFloat("Speed", animSpeed);
            }
        }
    }
}
