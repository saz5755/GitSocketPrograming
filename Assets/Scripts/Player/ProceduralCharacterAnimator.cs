using UnityEngine;

/// <summary>
/// CharacterModelBuilder 임시 모델에 절차적 보행 애니메이션 적용.
/// FBX 교체 후 이 컴포넌트와 CharacterModelBuilder를 삭제하면 됨.
/// </summary>
public class ProceduralCharacterAnimator : MonoBehaviour
{
    [Header("Walk Cycle")]
    [SerializeField] float walkFreq     = 1.8f;
    [SerializeField] float runFreq      = 3.2f;

    [Header("Leg")]
    [SerializeField] float walkLegSwing = 32f;
    [SerializeField] float runLegSwing  = 48f;
    [SerializeField] float kneeBendMax  = 38f;

    [Header("Arm")]
    [SerializeField] float walkArmSwing = 22f;
    [SerializeField] float runArmSwing  = 36f;

    [Header("Body Bob")]
    [SerializeField] float bodyBobAmp   = 0.03f;

    GroundController _gc;

    Transform _lUpperLeg, _rUpperLeg;
    Transform _lLowerLeg, _rLowerLeg;
    Transform _lUpperArm, _rUpperArm;
    Transform _lForearm,  _rForearm;
    Transform _body;

    Quaternion _origLUpperLeg, _origRUpperLeg;
    Quaternion _origLLowerLeg, _origRLowerLeg;
    Quaternion _origLUpperArm, _origRUpperArm;
    Quaternion _origLForearm,  _origRForearm;
    Vector3    _origBodyPos;

    float _phase;   // 보행 사이클 위상 (라디안)
    float _blend;   // 0=정지, 1=이동

    void Start()
    {
        _gc = GetComponent<GroundController>();

        _lUpperLeg = transform.Find("LUpperLeg");
        _rUpperLeg = transform.Find("RUpperLeg");
        _lLowerLeg = transform.Find("LLowerLeg");
        _rLowerLeg = transform.Find("RLowerLeg");
        _lUpperArm = transform.Find("LUpperArm");
        _rUpperArm = transform.Find("RUpperArm");
        _lForearm  = transform.Find("LForearm");
        _rForearm  = transform.Find("RForearm");
        _body      = transform.Find("UpperChest");

        _origLUpperLeg = GetRot(_lUpperLeg);
        _origRUpperLeg = GetRot(_rUpperLeg);
        _origLLowerLeg = GetRot(_lLowerLeg);
        _origRLowerLeg = GetRot(_rLowerLeg);
        _origLUpperArm = GetRot(_lUpperArm);
        _origRUpperArm = GetRot(_rUpperArm);
        _origLForearm  = GetRot(_lForearm);
        _origRForearm  = GetRot(_rForearm);
        _origBodyPos   = _body != null ? _body.localPosition : Vector3.zero;
    }

    void Update()
    {
        if (_gc == null) return;

        var state   = _gc.CurrentAnimState;
        bool moving = state == GroundAnimState.Walk || state == GroundAnimState.Run;
        bool running = state == GroundAnimState.Run;

        // 이동 블렌드 (정지 ↔ 이동 부드럽게 전환)
        _blend = Mathf.Lerp(_blend, moving ? 1f : 0f, Time.deltaTime * 9f);

        // 사이클 위상: 실제 속도 비례로 진행해서 발걸음이 이동 속도와 맞음
        float freq      = running ? runFreq : walkFreq;
        float speedRatio = _gc.CurrentSpeed / (running ? _gc.RunSpeed : _gc.WalkSpeed);
        _phase += Time.deltaTime * freq * Mathf.PI * 2f * Mathf.Clamp01(speedRatio);

        float s         = Mathf.Sin(_phase);
        float legSwing  = (running ? runLegSwing : walkLegSwing) * _blend;
        float armSwing  = (running ? runArmSwing : walkArmSwing) * _blend;
        float kneeMult  = kneeBendMax * _blend;

        // 다리: 좌우 반대 위상
        float legL =  s * legSwing;
        float legR = -s * legSwing;

        // 무릎: 해당 다리가 뒤로 갈 때 굽힘
        float kneeL = Mathf.Max(0f,  s) * kneeMult;
        float kneeR = Mathf.Max(0f, -s) * kneeMult;

        // 팔: 다리 반대
        float armL = -s * armSwing;
        float armR =  s * armSwing;

        SetRot(_lUpperLeg, _origLUpperLeg, legL,   0f, 0f);
        SetRot(_rUpperLeg, _origRUpperLeg, legR,   0f, 0f);
        SetRot(_lLowerLeg, _origLLowerLeg, -kneeL, 0f, 0f);
        SetRot(_rLowerLeg, _origRLowerLeg, -kneeR, 0f, 0f);
        SetRot(_lUpperArm, _origLUpperArm, armL,   0f, 0f);
        SetRot(_rUpperArm, _origRUpperArm, armR,   0f, 0f);
        SetRot(_lForearm,  _origLForearm,  armL * 0.4f, 0f, 0f);
        SetRot(_rForearm,  _origRForearm,  armR * 0.4f, 0f, 0f);

        // 몸통 상하 진동: 1 사이클당 2번 bounce
        if (_body != null)
        {
            float bobY = Mathf.Abs(s) * bodyBobAmp * _blend;
            _body.localPosition = _origBodyPos + new Vector3(0f, bobY, 0f);
        }
    }

    static Quaternion GetRot(Transform t) =>
        t != null ? t.localRotation : Quaternion.identity;

    static void SetRot(Transform t, Quaternion orig, float x, float y, float z)
    {
        if (t != null) t.localRotation = orig * Quaternion.Euler(x, y, z);
    }
}
