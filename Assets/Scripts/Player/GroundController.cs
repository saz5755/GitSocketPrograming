using UnityEngine;

/// <summary>
/// 지상 보행 캐릭터 컨트롤러.
/// 마우스 X → 캐릭터 수평 회전 / WASD → 이동 / 중력 적용.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class GroundController : MonoBehaviour
{
    [SerializeField] float walkSpeed = 5f;
    [SerializeField] float mouseSens = 2.5f;
    [SerializeField] float gravity   = -20f;

    CharacterController _cc;
    float _yaw;
    float _vy;

    void Awake() => _cc = GetComponent<CharacterController>();

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        _vy = 0f;
    }

    /// <summary>초기 수평 회전각 설정.</summary>
    public void InitYaw(float yaw) => _yaw = yaw;

    void Update()
    {
        _yaw += Input.GetAxis("Mouse X") * mouseSens;
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 dir = (transform.right * h + transform.forward * v).normalized;

        if (_cc.isGrounded && _vy < 0f) _vy = -2f;
        _vy += gravity * Time.deltaTime;

        _cc.Move((dir * walkSpeed + Vector3.up * _vy) * Time.deltaTime);
    }
}
