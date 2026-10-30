using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 엔진 비행운(contrail) — 지나간 하늘에 하얀 구름 줄이 오래 남는다.
///
/// 실제 비행운은 엔진 배기 수증기가 차가운 상층 공기에서 얼어붙어 생긴다 → 고도 조건.
/// 이 게임은 고도 스케일이 압축돼 있어 구름층 바닥(CloudLayer.cloudBase) 근처부터 생기게 맞췄다.
///  · 쌍발 노즐 뒤 두 줄 — 노즐 바로 뒤는 비어 있다가(배기가 식어 응결하기까지의 간격) 시작
///  · 시간이 지날수록 넓게 퍼지며 옅어진다 (약 25초)
///  · 고도를 벗어나면 그 자리에서 끊기고, 이미 그린 줄은 하늘에 남아 서서히 사라진다
/// 속도는 위치 변화로 추정하므로 로컬·원격·AI 기체 모두 같은 조건으로 동작한다.
/// </summary>
public class EngineContrailFX : MonoBehaviour
{
    // KF-21 쌍발 노즐 (기체 루트 로컬) — F35VFXController.SharedNozzleLocalPos 기준 좌우
    static readonly Vector3[] Nozzles = { new Vector3(-0.55f, 1.2f, -4.6f), new Vector3(0.55f, 1.2f, -4.6f) };

    const float TrailLife  = 25f;    // 하늘에 남는 시간
    const float MinSpeed   = 45f;    // 이 속도 이상 비행 중일 때만
    const float Hysteresis = 60f;    // 고도 경계에서 깜빡이지 않게

    static Material s_mat;
    static float s_onAlt = float.NaN;

    TrailRenderer[] _trails;
    PlayerController _pc;
    Vector3 _lastPos;
    float _speed;
    bool _on;

    public static EngineContrailFX Ensure(Transform aircraft)
    {
        if (aircraft == null || aircraft.Find("KF21_Model") == null) return null;
        var c = aircraft.GetComponent<EngineContrailFX>();
        return c != null ? c : aircraft.gameObject.AddComponent<EngineContrailFX>();
    }

    static float OnAltitude
    {
        get
        {
            if (float.IsNaN(s_onAlt))
            {
                var cl = FindAnyObjectByType<CloudLayer>();
                s_onAlt = (cl != null ? cl.cloudBase : 650f) + 50f;
            }
            return s_onAlt;
        }
    }

    void Awake()
    {
        if (s_mat == null)
        {
            var sh = Shader.Find("Custom/FlareSmoke");
            if (sh == null) { enabled = false; return; }
            s_mat = new Material(sh) { name = "Contrail", hideFlags = HideFlags.DontSave };
            s_mat.SetColor("_Color", new Color(1.2f, 1.22f, 1.25f, 1f));   // 햇빛 받은 얼음 결정 — 하늘보다 밝게
            s_mat.SetColor("_GlowTint", Color.black);   // 플레어용 붉은 머리 글로우 끔
            s_mat.SetFloat("_Opacity", 0.75f);
            s_mat.SetFloat("_Seed", 3.7f);
        }
        _trails = new TrailRenderer[Nozzles.Length];
        _pc = GetComponent<PlayerController>();
        _lastPos = transform.position;
    }

    TrailRenderer MakeTrail(int i)
    {
        var go = new GameObject("Contrail" + i);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Nozzles[i];
        var tr = go.AddComponent<TrailRenderer>();
        tr.sharedMaterial    = s_mat;
        tr.time              = TrailLife;
        tr.minVertexDistance = 3f;
        tr.widthMultiplier   = 1f;
        // 노즐 뒤 가늘게 시작 → 수 초 안에 굵어지고 → 오래될수록 수십 m 로 넓게 퍼진다
        tr.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(0.02f, 2.2f), new Keyframe(0.15f, 6f),
            new Keyframe(0.5f, 13f), new Keyframe(1f, 24f));
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.012f),
                          new GradientAlphaKey(0.75f, 0.3f), new GradientAlphaKey(0.35f, 0.7f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient     = g;
        tr.alignment         = LineAlignment.View;
        tr.textureMode       = LineTextureMode.Stretch;
        tr.numCapVertices    = 0;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows    = false;
        tr.autodestruct      = false;
        tr.emitting          = true;
        return tr;
    }

    /// <summary>지금 그리던 줄을 하늘에 남겨 두고 떼어낸다 — 남은 점들이 수명을 다하면 스스로 사라진다</summary>
    void Detach()
    {
        for (int i = 0; i < _trails.Length; i++)
        {
            var tr = _trails[i];
            if (tr == null) continue;
            tr.emitting = false;
            tr.transform.SetParent(null, true);
            tr.autodestruct = true;
            Destroy(tr.gameObject, TrailLife + 1f);   // 안전장치
            _trails[i] = null;
        }
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || _trails == null) return;

        Vector3 pos = transform.position;
        float jump = (pos - _lastPos).magnitude;
        _lastPos = pos;
        if (jump / dt > 600f)
        {
            // 리스폰·탑승 순간이동 — 이전 위치와 잇는 긴 띠가 생기지 않게 끊는다
            Detach();
            _on = false;
            return;
        }
        _speed = Mathf.Lerp(_speed, jump / dt, 1f - Mathf.Exp(-dt * 3f));

        float alt = pos.y - FXLib.SeaY;
        // 조종 불능(잔해 낙하)이면 엔진이 멈춘 것 — 비행운 대신 WreckController 연기가 남는다
        // 에어쇼 연막을 뿜는 동안은 연막이 배기를 덮는다 — 흰 비행운이 컬러 연막에 섞여 색이 바래지 않게
        bool smoking = _pc != null && AirshowSmokeFX.IsOn(_pc.SmokeFx);
        bool want = _speed > MinSpeed && !smoking && (_pc == null || (_pc.isLocalPlayer ? _pc.enabled : !_pc.IsWreck))
                 && (_on ? alt > OnAltitude - Hysteresis : alt > OnAltitude);

        if (want && !_on)
            for (int i = 0; i < _trails.Length; i++) _trails[i] = MakeTrail(i);
        else if (!want && _on)
            Detach();
        _on = want;
    }

    void OnDisable()
    {
        // 비활성 중엔 계층을 바꿀 수 없어 떼어내지 않고 방출만 멈춘다 (평소 끊김은 LateUpdate 에서 Detach)
        if (_trails != null)
            foreach (var tr in _trails) if (tr != null) tr.emitting = false;
    }

    void OnEnable()
    {
        if (_trails != null)
            foreach (var tr in _trails) if (tr != null) tr.emitting = true;
    }
}
