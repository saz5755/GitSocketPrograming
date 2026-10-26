using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 고속 비행 시 날개 끝에서 끌리는 증기 꼬리(윙팁 보텍스 응결).
///
/// 물리적 근거: 실제 하얀 꼬리는 "음속 돌파" 자체로 생기지 않는다.
///  · 비행운(contrail) — 엔진 배기 수증기가 고고도(-40°C 이하, 약 8km+)에서 얼어붙는 현상. 속도와 무관
///  · 증기 원뿔(vapor cone) — 천음속(마하 0.9~1.0) 습한 공기에서 순간적으로 보이는 원뿔. 지속되는 꼬리가 아님
///  · 윙팁/LEX 보텍스 응결 — 고속·고G 기동 중 날개 끝 소용돌이의 저압부에서 습기가 응결해 줄처럼 끌림 ← 이 게임 고도(수백 m)·속도에서 사실적인 것
/// 이 게임의 최고 속도는 애프터버너 시 약 120 m/s(≈ 마하 0.35)라 음속 기준은 성립하지 않는다.
/// 그래서 "애프터버너로만 도달 가능한 고속(≥ 105 m/s)을 유지하는 동안" 발생하고, 선회 G 가 클수록 진해진다.
/// 속도는 위치 변화로 추정하므로 로컬/원격/AI 기체 모두 동일하게 동작한다.
/// </summary>
public class VaporTrailRig : MonoBehaviour
{
    // KF21 기체 루트 로컬 — 주익 끝 뒷전 (모델 실측)
    static readonly Vector3[] Tips = { new Vector3(-2.75f, 1.22f, -2.0f), new Vector3(2.75f, 1.22f, -2.0f) };

    const float OnSpeed  = 105f;   // 이 속도 이상 유지 시 발생 (기본 최고속 80, AB 최고속 120)
    const float FullSpeed = 116f;
    const float OffSpeed = 99f;    // 이 속도 아래로 떨어지면 서서히 사라짐 (히스테리시스)
    const float TrailTime = 1.5f;   // 실제 윙팁 응결은 수백 m 안에서 흩어진다

    static Material s_mat;
    static readonly int ID_Fade = Shader.PropertyToID("_Fade");

    TrailRenderer[] _trails;
    MaterialPropertyBlock _mpb;
    Vector3 _lastPos, _lastFwd;
    float _speed, _turn, _amount;
    bool _on;

    public static VaporTrailRig Ensure(Transform aircraft)
    {
        if (aircraft == null || aircraft.Find("KF21_Model") == null) return null;
        var r = aircraft.GetComponent<VaporTrailRig>();
        return r != null ? r : aircraft.gameObject.AddComponent<VaporTrailRig>();
    }

    void Awake()
    {
        if (s_mat == null)
        {
            var sh = Shader.Find("Custom/FlareSmoke");
            if (sh == null) { enabled = false; return; }
            s_mat = new Material(sh) { name = "VaporTrail" };
            s_mat.SetColor("_Color", new Color(0.93f, 0.95f, 0.98f, 1f));
            s_mat.SetColor("_GlowTint", Color.black);       // 플레어용 붉은 머리 글로우 끔
            s_mat.SetFloat("_Opacity", 0.36f);
        }
        _mpb = new MaterialPropertyBlock();
        _trails = new TrailRenderer[Tips.Length];
        for (int i = 0; i < Tips.Length; i++)
        {
            var go = new GameObject("VaporTrail" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Tips[i];
            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = s_mat;
            tr.time = TrailTime;
            tr.minVertexDistance = 0.8f;
            tr.widthMultiplier = 1f;
            // 날개 끝에서 가늘게 시작해 소용돌이가 풀리며 넓어진다
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.25f, 0.38f), new Keyframe(1f, 1.2f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.3f), new GradientAlphaKey(0.12f, 0.7f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.numCapVertices = 0;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            _trails[i] = tr;
        }
        _lastPos = transform.position;
        _lastFwd = transform.forward;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || _trails == null) return;   // 셰이더를 못 찾아 초기화를 건너뛴 경우

        float v = (transform.position - _lastPos).magnitude / dt;
        if (v > 400f)                                                // 순간이동(리스폰·탑승): 이전 위치와 잇는 긴 띠가 생기지 않게 지운다
        {
            v = _speed;
            foreach (var tr in _trails) tr.Clear();
        }
        _speed = Mathf.Lerp(_speed, v, 1f - Mathf.Exp(-dt * 4f));
        float turnRate = Vector3.Angle(_lastFwd, transform.forward) / dt;   // deg/s
        _turn = Mathf.Lerp(_turn, turnRate, 1f - Mathf.Exp(-dt * 5f));
        _lastPos = transform.position;
        _lastFwd = transform.forward;

        if (!_on && _speed >= OnSpeed) _on = true;
        else if (_on && _speed < OffSpeed) _on = false;

        // 하중계수 근사: 선회 각속도 × 속도 (n ≈ v·ω/g)
        float g = _speed * _turn * Mathf.Deg2Rad / 9.81f;
        float target = _on ? Mathf.InverseLerp(OffSpeed, FullSpeed, _speed) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01((g - 1f) / 3f)) : 0f;
        _amount = Mathf.MoveTowards(_amount, target, dt * (target > _amount ? 0.8f : 0.6f));

        bool emit = _amount > 0.03f;
        _mpb.SetFloat(ID_Fade, _amount);
        foreach (var tr in _trails)
        {
            if (tr.emitting != emit)
            {
                if (emit) tr.Clear();   // 끊겼다 다시 시작할 때 이전 꼬리와 이어지지 않게
                tr.emitting = emit;
            }
            tr.widthMultiplier = Mathf.Lerp(0.7f, 1f, _amount);
            tr.SetPropertyBlock(_mpb);
        }
    }
}
