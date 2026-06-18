using UnityEngine;

/// <summary>
/// 원격 플레이어 지상 캐릭터의 위치·회전을 부드럽게 보간하고
/// Animator가 있으면 수신된 animState로 걷기/달리기 애니메이션을 구동한다.
/// </summary>
public class RemoteGroundInterp : MonoBehaviour
{
    Vector3    _targetPos;
    Quaternion _targetRot;
    bool       _initialized;

    static readonly int s_hashState = Animator.StringToHash("State");
    static readonly int s_hashSpeed = Animator.StringToHash("Speed");

    Animator                    _anim;
    ProceduralCharacterAnimator _procAnim;
    bool                        _hasStateParam;
    bool                        _hasSpeedParam;
    float                       _lastAnimUpdateTime;

    Vector3 _initialScale;
    (Transform t, Vector3 scale)[] _childScales;

    void Awake()
    {
        _initialScale = transform.localScale;

        // 직계 자식 초기 스케일 저장 (Armature 등 Animator가 변경하는 본 루트 포함)
        var children = new System.Collections.Generic.List<(Transform, Vector3)>();
        foreach (Transform child in transform)
            children.Add((child, child.localScale));
        _childScales = children.ToArray();

        _anim = GetComponentInChildren<Animator>();
        if (_anim != null)
        {
            foreach (var p in _anim.parameters)
            {
                if (p.name == "State") _hasStateParam = true;
                if (p.name == "Speed") _hasSpeedParam = true;
            }
        }

        _procAnim = GetComponent<ProceduralCharacterAnimator>();
        if (_procAnim != null) _procAnim.SetRemoteMode(true);

        _lastAnimUpdateTime = Time.time;
    }

    // SetActive(false) → 재활성 시 반드시 텔레포트하도록 초기화 상태 리셋
    void OnDisable() => _initialized = false;

    // animState: 0=Idle 1=Walk 2=Run 3=Jump  (GroundAnimState 열거형과 동일)
    public void SetTarget(Vector3 pos, Quaternion rot, int animState = 0)
    {
        if (!_initialized)
        {
            // 첫 수신(또는 재활성화 직후): lerp 없이 즉시 텔레포트
            transform.SetPositionAndRotation(pos, rot);
            _initialized = true;
        }
        _targetPos = pos;
        _targetRot = rot;

        ApplyAnim(animState);
    }

    void ApplyAnim(int animState)
    {
        // 패킷 수신 간격을 직접 측정해 dampTime 진행량 계산 (Time.deltaTime은 렌더 프레임 간격으로 부정확)
        float now = Time.time;
        float dt  = Mathf.Clamp(now - _lastAnimUpdateTime, 0.01f, 0.2f);
        _lastAnimUpdateTime = now;

        // 절차적 애니메이터가 있으면 위임 (FBX Animator 없는 procedural 모델용)
        if (_procAnim != null)
        {
            _procAnim.SetRemoteAnimState((GroundAnimState)animState);
            return;
        }

        if (_anim == null) return;

        if (_hasStateParam)
            _anim.SetInteger(s_hashState, animState);

        if (_hasSpeedParam)
        {
            // Soldier.controller BlendTree 매핑: 0=Idle, 0.5=Walk, 1.0=Run
            float speed = animState switch
            {
                1 => 0.5f,   // Walk
                2 => 1.0f,   // Run
                _ => 0.0f    // Idle / Jump
            };
            _anim.SetFloat(s_hashSpeed, speed, 0.12f, dt);
        }
    }

    // LateUpdate: Animator가 Update phase에서 스케일을 변경한 이후에 실행되어 원복 보장
    void LateUpdate()
    {
        if (!_initialized) return;
        transform.position = Vector3.Lerp(transform.position, _targetPos, 18f * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, 18f * Time.deltaTime);

        // 루트 스케일 고정
        if (transform.localScale != _initialScale) transform.localScale = _initialScale;
        // 직계 자식 스케일 고정 (Armature 본 등 Animator가 변경하는 대상)
        if (_childScales != null)
            foreach (var (t, scale) in _childScales)
                if (t != null && t.localScale != scale) t.localScale = scale;
    }
}
