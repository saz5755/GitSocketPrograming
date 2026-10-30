using UnityEngine;

/// <summary>
/// 외부 조명 — 항법등(좌 적색 · 우 녹색 · 꼬리 백색)과 날개 끝 충돌방지 스트로브(이중 섬광).
/// 실제 기체처럼 엔진이 돌 때만 켜지고, 낮에는 은은하고 해가 낮을수록 또렷하다.
/// FXFlare 빌보드라 수 km 밖에서도 점으로 보여 편대·적기 위치 감각을 돕는다 (DCS·MSFS 공통 연출).
/// </summary>
public class AircraftLightsFX : MonoBehaviour
{
    // KF-21 기체 루트 로컬 — VaporTrailRig 의 날개 끝 실측값 기준
    static readonly Vector3 LeftTip  = new Vector3(-2.82f, 1.22f, -1.9f);
    static readonly Vector3 RightTip = new Vector3( 2.82f, 1.22f, -1.9f);

    static readonly Color NavRed   = new Color(1f, 0.08f, 0.04f);
    static readonly Color NavGreen = new Color(0.1f, 1f, 0.35f);
    static readonly Color NavWhite = new Color(1f, 0.95f, 0.88f);
    static readonly Color Strobe   = new Color(0.9f, 0.95f, 1f);

    PlayerController _pc;
    F35VFXController _vfx;
    AircraftEngineAudio _engine;
    FXLib.Flare _left, _right, _tail, _strobeL, _strobeR;
    float _phase, _on;

    public static AircraftLightsFX Ensure(Transform aircraft)
    {
        if (aircraft == null || aircraft.Find("KF21_Model") == null) return null;
        var l = aircraft.GetComponent<AircraftLightsFX>();
        return l != null ? l : aircraft.gameObject.AddComponent<AircraftLightsFX>();
    }

    void Start()
    {
        _pc  = GetComponent<PlayerController>();
        _vfx = GetComponent<F35VFXController>();
        _phase = Random.value * 1.3f;

        _left    = FXLib.MakeFlare("NavL", transform, LeftTip,  0.22f, 0.0016f);
        _right   = FXLib.MakeFlare("NavR", transform, RightTip, 0.22f, 0.0016f);
        _strobeL = FXLib.MakeFlare("StrobeL", transform, LeftTip  + new Vector3(-0.05f, 0f, -0.15f), 0.35f, 0.0024f, 0.6f);
        _strobeR = FXLib.MakeFlare("StrobeR", transform, RightTip + new Vector3( 0.05f, 0f, -0.15f), 0.35f, 0.0024f, 0.6f);
        _tail    = FXLib.MakeFlare("NavTail", transform, TailPos(), 0.2f, 0.0014f);
    }

    // 꼬리등 — 모델 바운드 최후방 (노즐 위)
    Vector3 TailPos()
    {
        var model = transform.Find("KF21_Model");
        var rs = model != null ? model.GetComponentsInChildren<MeshRenderer>() : null;
        if (rs == null || rs.Length == 0) return new Vector3(0f, 1.6f, -5.2f);
        float minZ = float.MaxValue;
        foreach (var r in rs)
        {
            var b = r.bounds;
            Vector3 c = transform.InverseTransformPoint(b.center);
            Vector3 e = transform.InverseTransformVector(b.extents);
            minZ = Mathf.Min(minZ, c.z - Mathf.Abs(e.z));
        }
        return new Vector3(0f, 1.55f, minZ + 0.35f);
    }

    void Update()
    {
        if (_pc == null || _left == null) return;
        var gm = GameModeManager.Instance;
        bool running;
        if (_pc.isLocalPlayer) running = (gm != null && gm.IsFlying && !gm.IsDead && _pc.enabled) || (_vfx != null && _vfx.EngineIdleOn);
        else
        {
            if (_engine == null) _engine = GetComponent<AircraftEngineAudio>();
            running = _engine != null && _engine.EngineRunning;   // 원격·봇: 엔진 사운드와 같은 판정 (주기 중엔 소등)
        }

        _on = Mathf.MoveTowards(_on, running ? 1f : 0f, Time.deltaTime * 3f);

        // 낮엔 약하게, 해가 낮을수록(새벽·석양·밤) 밝게
        var sun = RenderSettings.sun;
        float elev = sun != null ? Mathf.Clamp01(-sun.transform.forward.y) : 0.5f;
        float dayDim = Mathf.Lerp(1.8f, 0.9f, Mathf.Clamp01(elev * 2.5f));
        float nav = 3.2f * dayDim * _on;

        _left.Set(NavRed, nav);
        _right.Set(NavGreen, nav);
        _tail.Set(NavWhite, nav * 0.8f);

        // 스트로브: 1.3초 주기 이중 섬광
        _phase += Time.deltaTime;
        if (_phase > 1.3f) _phase -= 1.3f;
        bool flash = _phase < 0.05f || (_phase > 0.16f && _phase < 0.21f);
        float st = flash && running ? 14f * dayDim : 0f;
        _strobeL.Set(Strobe, st);
        _strobeR.Set(Strobe, st);
    }
}
