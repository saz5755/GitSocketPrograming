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

    Animator _anim;
    bool     _hasStateParam;
    bool     _hasSpeedParam;

    void Awake()
    {
        _anim = GetComponentInChildren<Animator>();
        if (_anim == null) return;

        foreach (var p in _anim.parameters)
        {
            if (p.name == "State") _hasStateParam = true;
            if (p.name == "Speed") _hasSpeedParam = true;
        }
    }

    // animState: 0=Idle 1=Walk 2=Run 3=Jump  (GroundAnimState 열거형과 동일)
    public void SetTarget(Vector3 pos, Quaternion rot, int animState = 0)
    {
        if (!_initialized)
        {
            transform.SetPositionAndRotation(pos, rot);
            _initialized = true;
        }
        _targetPos = pos;
        _targetRot = rot;

        ApplyAnim(animState);
    }

    void ApplyAnim(int animState)
    {
        if (_anim == null) return;

        if (_hasStateParam)
            _anim.SetInteger("State", animState);

        if (_hasSpeedParam)
        {
            // Soldier.controller BlendTree 매핑: 0=Idle, 0.5=Walk, 1.0=Run
            float speed = animState switch
            {
                1 => 0.5f,   // Walk
                2 => 1.0f,   // Run
                _ => 0.0f    // Idle / Jump
            };
            _anim.SetFloat("Speed", speed);
        }
    }

    void Update()
    {
        if (!_initialized) return;
        transform.position = Vector3.Lerp(transform.position, _targetPos, 18f * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, 18f * Time.deltaTime);
    }
}
