using UnityEngine;

public class FlightCamera : MonoBehaviour
{
    [Header("Chase Camera")]
    [SerializeField] Vector3 chaseOffset = new Vector3(0f, 3f, -12f);
    [SerializeField] float chaseSmooth = 6f;

    [Header("Cockpit Camera")]
    [SerializeField] Vector3 cockpitOffset = new Vector3(0f, 0.26f, 0.80f);

    [Header("Free Look (우클릭 드래그)")]
    [SerializeField] float freeLookSensitivity = 3f;
    [SerializeField] float freeLookReturnSpeed = 8f;
    [SerializeField] float freeLookMaxPitch    = 80f;

    [Header("Toggle")]
    [SerializeField] KeyCode toggleKey = KeyCode.C;

    bool isCockpit = false;
    public bool IsCockpit => isCockpit;
    PlayerController localPlayer;
    Renderer[] localRenderers;
    CockpitBuilder cockpitBuilder;

    float freeLookYaw   = 0f;
    float freeLookPitch = 0f;

    float _shakeMag, _shakeTimer, _shakeDur;

    // ── 지상 모드 ──────────────────────────────────────────────────────────
    Transform        _groundTarget;
    GroundController _groundController;
    float            _groundPitch = 10f;

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
        cockpitBuilder = gameObject.AddComponent<CockpitBuilder>();
    }

    void Start()
    {
        FindLocalPlayer();
    }

    void FindLocalPlayer()
    {
        foreach (PlayerController pc in FindObjectsOfType<PlayerController>())
        {
            if (pc.isLocalPlayer)
            {
                localPlayer = pc;
                localRenderers = pc.GetComponentsInChildren<Renderer>();
                break;
            }
        }
    }

    // ── 외부 API ────────────────────────────────────────────────────────────
    public void SetGroundTarget(Transform t)
    {
        _groundTarget     = t;
        _groundController = t != null ? t.GetComponent<GroundController>() : null;
        _groundPitch      = 10f;
    }

    public void SetFlightTarget(PlayerController pc)
    {
        _groundTarget  = null;
        localPlayer    = pc;
        localRenderers = pc.GetComponentsInChildren<Renderer>();
    }

    void LateUpdate()
    {
        // 지상 모드 우선 처리
        if (_groundTarget != null)
        {
            UpdateGround();
            return;
        }

        if (localPlayer == null)
        {
            FindLocalPlayer();
            return;
        }

        if (Input.GetKeyDown(toggleKey))
            SetCockpit(!isCockpit);

        // 우클릭 중 자유시점: 마우스로 카메라 회전, 손을 떼면 원위치로 복귀
        bool freeLook = Input.GetMouseButton(1);

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

        // 카메라 쉐이크
        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;
            float decay = _shakeDur > 0f ? _shakeTimer / _shakeDur : 0f;
            transform.position += Random.insideUnitSphere * (_shakeMag * decay);
        }
    }

    // ── 지상 3인칭 카메라 ──────────────────────────────────────────────────
    void UpdateGround()
    {
        if (_groundTarget == null) return;

        // 마우스 Y → 카메라 피치 (캐릭터 회전과 독립)
        _groundPitch -= Input.GetAxis("Mouse Y") * freeLookSensitivity;
        _groundPitch  = Mathf.Clamp(_groundPitch, -15f, 55f);

        // 카메라 수평은 GroundController.CameraYaw 사용 (캐릭터 facing과 분리)
        float cameraYaw = _groundController != null
            ? _groundController.CameraYaw
            : _groundTarget.eulerAngles.y;
        Quaternion camRot = Quaternion.Euler(_groundPitch, cameraYaw, 0f);

        Vector3 targetPos = _groundTarget.position + camRot * groundOffset;

        // 지형 관통 방지 (SphereCast)
        Vector3 dir = targetPos - _groundTarget.position;
        if (Physics.SphereCast(_groundTarget.position + Vector3.up * 1.5f,
                               0.3f, dir.normalized, out RaycastHit hit, dir.magnitude))
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
        Quaternion freeLookOffset = Quaternion.Euler(freeLookPitch, freeLookYaw, 0f);
        transform.rotation = t.rotation * freeLookOffset;
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

    void SetCockpit(bool cockpit)
    {
        isCockpit = cockpit;
        if (localRenderers != null)
            foreach (Renderer r in localRenderers)
                r.enabled = !cockpit;
        cockpitBuilder?.SetVisible(cockpit);
    }
}
