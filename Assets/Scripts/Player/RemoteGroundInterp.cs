using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 원격 플레이어 지상 캐릭터의 위치·회전을 부드럽게 보간하고
/// Animator가 있으면 수신된 animState로 걷기/달리기 애니메이션을 구동한다.
/// </summary>
public class RemoteGroundInterp : MonoBehaviour
{
    Vector3    _targetPos;
    Quaternion _targetRot;
    bool       _initialized;
    Vector3    _posVel;

    static readonly int s_hashState = Animator.StringToHash("State");
    static readonly int s_hashSpeed = Animator.StringToHash("Speed");

    Animator                    _anim;
    ProceduralCharacterAnimator _procAnim;
    bool                        _hasStateParam;
    bool                        _hasSpeedParam;
    float                       _targetAnimSpeed;

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
            _anim.applyRootMotion = false;
            foreach (var p in _anim.parameters)
            {
                if (p.name == "State") _hasStateParam = true;
                if (p.name == "Speed") _hasSpeedParam = true;
            }
            ForceLoopLocomotionClips(_anim);
        }

        _procAnim = GetComponent<ProceduralCharacterAnimator>();
        if (_procAnim != null) _procAnim.SetRemoteMode(true);
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

    // SetActive(false) → 재활성 시 반드시 텔레포트하도록 초기화 상태 리셋
    void OnDisable() { _initialized = false; _posVel = Vector3.zero; }

    // Update: Animator 평가 이전에 Speed를 갱신해야 이 프레임에서 즉시 반영됨
    void Update()
    {
        if (!_initialized || _anim == null || !_hasSpeedParam) return;
        // dampTime=0.08f: ~0.25s 내 목표 도달 (20Hz 패킷 간격보다 빠름 → 부드러운 보간)
        _anim.SetFloat(s_hashSpeed, _targetAnimSpeed, 0.08f, Time.deltaTime);
    }

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
        // animState 인코딩: 0~100 = animSpeed × 100 (연속), 300 = Jump
        bool isJump = animState >= 300;

        // 절차적 애니메이터 경로 (DrStrange 프리팹 없을 때 폴백)
        if (_procAnim != null)
        {
            // encodedAnim 50 = walkSpeed 도달 지점(animSpeed=0.5) — 로컬과 동일한 Run 임계값
            GroundAnimState state = isJump          ? GroundAnimState.Jump
                : animState >= 50               ? GroundAnimState.Run
                : animState >= 20               ? GroundAnimState.Walk
                : GroundAnimState.Idle;
            _procAnim.SetRemoteAnimState(state);
            return;
        }

        if (_anim == null) return;

        // State 파라미터: Jump 전환 전용 (3=Jump, 0=기타)
        if (_hasStateParam)
            _anim.SetInteger(s_hashState, isJump ? 3 : 0);

        // Speed 파라미터: 수신된 연속값을 BlendTree에 직접 반영
        if (_hasSpeedParam)
            _targetAnimSpeed = isJump ? 0f : Mathf.Clamp01(animState / 100f);
    }

    // LateUpdate: Animator가 Update phase에서 스케일을 변경한 이후에 실행되어 원복 보장
    void LateUpdate()
    {
        if (!_initialized) return;
        // SmoothDamp: 패킷 도착 시 목표 위치가 갑자기 바뀌어도 속도를 연속적으로 유지
        // smoothTime=0.12f: 패킷 간격(0.05s)의 2배 이상 → 패킷마다 즉각 반응하지 않아 덜덜거림 억제
        transform.position = Vector3.SmoothDamp(transform.position, _targetPos, ref _posVel, 0.12f);
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, 12f * Time.deltaTime);

        // 루트 스케일 고정
        if (transform.localScale != _initialScale) transform.localScale = _initialScale;
        // 직계 자식 스케일 고정 (Armature 본 등 Animator가 변경하는 대상)
        if (_childScales != null)
            foreach (var (t, scale) in _childScales)
                if (t != null && t.localScale != scale) t.localScale = scale;
    }
}
