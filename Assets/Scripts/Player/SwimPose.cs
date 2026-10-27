using UnityEngine;

/// <summary>
/// 입영(treading water) 자세 — 수영 클립이 없는 Generic 리그용 절차 애니메이션.
/// Animator 가 Idle 을 적용한 뒤 LateUpdate 에서 팔·다리 뼈를 캐릭터 기준 방향으로 겨눠 덮어쓴다
/// (뼈 로컬 축이 모델마다 달라 각도 대신 "부모 관절 → 자식 관절" 방향을 맞춘다).
///  · 팔: 어깨 높이 바로 아래로 옆·앞으로 뻗고, 아래팔이 안팎으로 8자를 그리며 물을 젓는다 (sculling)
///  · 다리: 허벅지를 앞으로 들어 앉은 듯한 자세에서 정강이가 좌우 엇갈려 원을 그린다 (eggbeater kick)
/// 이동 속도가 붙으면 주기가 빨라진다. Active 를 끄면 서서히 Idle 로 돌아간다.
/// </summary>
public class SwimPose : MonoBehaviour
{
    public bool  Active { get; set; }
    /// <summary>수평 이동 속도 (m/s) — 주기·진폭에 반영</summary>
    public float Speed  { get; set; }

    [SerializeField] float idlePeriod = 1.7f;
    [SerializeField] float fastPeriod = 0.95f;
    [SerializeField] float fadeRate   = 2.5f;

    Transform _armL, _foreL, _handL, _armR, _foreR, _handR;
    Transform _thighL, _shinL, _footL, _thighR, _shinR, _footR;
    bool  _ok;
    float _w, _phase;

    void Awake()
    {
        Transform F(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }
        _armL = F("LeftArm");  _foreL = F("LeftForeArm");  _handL = F("LeftHand");
        _armR = F("RightArm"); _foreR = F("RightForeArm"); _handR = F("RightHand");
        _thighL = F("LeftUpLeg");  _shinL = F("LeftLeg");  _footL = F("LeftFoot");
        _thighR = F("RightUpLeg"); _shinR = F("RightLeg"); _footR = F("RightFoot");
        _ok = _armL && _foreL && _handL && _armR && _foreR && _handR && _thighL && _shinL && _footL && _thighR && _shinR && _footR;
    }

    void LateUpdate()
    {
        if (!_ok) return;
        float dt = Time.deltaTime;
        _w = Mathf.MoveTowards(_w, Active ? 1f : 0f, fadeRate * dt);
        if (_w <= 0f) return;

        float k = Mathf.Clamp01(Speed / 1.9f);
        _phase += dt * 2f * Mathf.PI / Mathf.Lerp(idlePeriod, fastPeriod, k);
        float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
        float w = Mathf.SmoothStep(0f, 1f, _w);

        Vector3 up = transform.up, fwd = transform.forward, right = transform.right;

        // 팔: 위팔은 옆·약간 앞·약간 아래, 아래팔은 앞쪽에서 안팎으로 젓기 (좌우 대칭)
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 sd = right * side;
            var arm  = side < 0 ? _armL  : _armR;
            var fore = side < 0 ? _foreL : _foreR;
            var hand = side < 0 ? _handL : _handR;
            Aim(arm,  fore, sd * 0.85f + fwd * (0.35f + 0.18f * s) - up * (0.38f + 0.08f * c), w);
            Aim(fore, hand, fwd * 0.75f + sd * (0.30f + 0.42f * s) - up * (0.22f + 0.10f * c), w);
        }

        // 다리: 허벅지는 앞으로 들고 살짝 벌림, 정강이는 원을 그리되 좌우 반 박자 엇갈림
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 sd = right * side;
            float ps = side < 0 ? s : -s, pc = side < 0 ? c : -c;
            var thigh = side < 0 ? _thighL : _thighR;
            var shin  = side < 0 ? _shinL  : _shinR;
            var foot  = side < 0 ? _footL  : _footR;
            Aim(thigh, shin, -up * 0.75f + fwd * (0.55f + 0.12f * ps) + sd * 0.30f, w);
            Aim(shin,  foot, -up + sd * (0.30f + 0.32f * pc) + fwd * (0.05f + 0.28f * ps), w);
        }
    }

    // 관절 bone 에서 child 로 향하는 방향을 dir 로 돌린다 (weight 만큼)
    static void Aim(Transform bone, Transform child, Vector3 dir, float weight)
    {
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f || dir.sqrMagnitude < 1e-8f) return;
        Quaternion delta = Quaternion.FromToRotation(cur, dir);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
    }
}
