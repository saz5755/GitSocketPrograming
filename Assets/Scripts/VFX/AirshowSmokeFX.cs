using UnityEngine;

/// <summary>
/// 에어쇼 스모크 (연막) — 블랙이글스·블루엔젤스처럼 배기구에 연막유를 분사해 짙은 연기 줄을 남긴다.
///   · 노즐 바로 뒤에서 촘촘하고 진하게 시작해 수 초에 걸쳐 굵게 퍼지며 바람에 밀린다 (약 14초)
///   · 흰색 + 컬러 (Palette) — 편대가 같은 순간 켜고 꺼서 하늘에 그림을 그린다
///   · 상태는 PlayerController.SmokeFx (bit0 ON, bit1~3 색) 하나만 본다
///       로컬 = AirshowController 입력, 호스트 봇 = 편대 지시, 원격 = 보간 중인 스냅샷 값
///     → 원격 기체는 보간된 위치가 실제로 켠 지점을 지날 때 켜지므로 다른 화면에서도 같은 자리에서 시작·끝난다
/// </summary>
public class AirshowSmokeFX : MonoBehaviour
{
    public static readonly Color[] Palette =
    {
        new Color(0.97f, 0.97f, 0.97f),   // 0 백색
        new Color(0.95f, 0.16f, 0.14f),   // 1 적색
        new Color(0.16f, 0.36f, 0.95f),   // 2 청색
        new Color(1.00f, 0.80f, 0.12f),   // 3 황색
        new Color(0.20f, 0.80f, 0.35f),   // 4 녹색
        new Color(0.95f, 0.45f, 0.10f),   // 5 주황
        new Color(0.62f, 0.28f, 0.90f),   // 6 보라
        new Color(1.00f, 0.45f, 0.75f),   // 7 분홍
    };

    public static byte Encode(bool on, int color) => (byte)((on ? 1 : 0) | ((color & 7) << 1));
    public static bool IsOn(byte fx)     => (fx & 1) != 0;
    public static int  ColorOf(byte fx)  => (fx >> 1) & 7;   // bit4 는 공중급유 접속 (PlayerController.FxRefuelBit)

    const float Life = 12f;
    // KF-21 쌍발 노즐 사이 (기체 루트 로컬)
    static readonly Vector3 Nozzle = new Vector3(0f, 1.2f, -5.2f);

    PlayerController _pc;
    ParticleSystem _ps;
    Transform _emitter;
    byte _applied = 0xFF;
    Vector3 _lastPos;

    public static AirshowSmokeFX Ensure(Transform aircraft)
    {
        if (aircraft == null || aircraft.Find("KF21_Model") == null) return null;
        var s = aircraft.GetComponent<AirshowSmokeFX>();
        return s != null ? s : aircraft.gameObject.AddComponent<AirshowSmokeFX>();
    }

    void Start()
    {
        _pc = GetComponent<PlayerController>();
        _lastPos = transform.position;
    }

    void Build()
    {
        // 연기는 기체와 독립된 월드 오브젝트 — 기체가 사라지거나(격추·퇴장) 순간이동해도 줄은 하늘에 남는다
        _ps = FXLib.MakePS("AirshowSmoke_" + name, null, FXLib.AirshowSmoke, 1800, worldSpace: true, loop: true);   // 상한 > 방출률×수명 (상한에 걸리면 방출이 멈춰 틈이 생긴다)
        _emitter = _ps.transform;
        var m = _ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(Life * 0.75f, Life);
        m.startSpeed    = new ParticleSystem.MinMaxCurve(0f, 0.6f);
        m.startSize     = new ParticleSystem.MinMaxCurve(2.4f, 3.2f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.gravityModifier = -0.003f;
        var em = _ps.emission; em.rateOverTime = 0f; em.rateOverDistance = 1.1f;   // 100 m/s × 12 s ≈ 1300 입자
        var sh = _ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
        var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);   // 일정한 바람
        vel.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        var rot = _ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        // 노즐 직후는 배기 흐름에 눌려 가늘다 → 1초 안에 굵어지고 → 천천히 크게 풀어진다
        FXLib.SizeOverLife(_ps, FXLib.Curve(0f, 0.8f, 0.06f, 1.35f, 0.4f, 2.6f, 1f, 4.6f));
        FXLib.ColorOverLife(_ps, FXLib.Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f },
            new[] { 0.95f, 0.85f, 0.45f, 0f }, new[] { 0f, 0.15f, 0.6f, 1f }));
    }

    void LateUpdate()
    {
        if (_pc == null) return;
        byte fx = _pc.enabled || !_pc.isLocalPlayer ? _pc.SmokeFx : (byte)0;   // 잔해·하차 시 끔
        bool on = IsOn(fx);

        Vector3 pos = transform.position;
        bool teleport = (pos - _lastPos).sqrMagnitude > 400f * 400f;
        _lastPos = pos;

        if (on && _ps == null) Build();
        if (_ps == null) return;

        if (teleport && _ps.isEmitting)
        {
            // 순간이동 구간을 연기로 잇지 않도록 한 번 끊는다
            _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _applied = 0xFF;
        }
        _emitter.SetPositionAndRotation(transform.TransformPoint(Nozzle), transform.rotation);

        if (fx == _applied) return;
        _applied = fx;
        if (on)
        {
            var m = _ps.main;
            m.startColor = Palette[ColorOf(fx)];
            if (!_ps.isEmitting) _ps.Play();
        }
        else _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    void OnDestroy()
    {
        if (_ps != null)
        {
            _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(_ps.gameObject, Life + 1f);
        }
    }
}
