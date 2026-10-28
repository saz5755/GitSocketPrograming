using UnityEngine;

/// <summary>
/// 음성 경고 (v4.1 F-3) — 실기 "Bitching Betty" 처럼 우선순위 큐로 한 번에 하나만 말한다.
/// 우선순위: PULL UP > MISSILE > STALL > OVER G > ALTITUDE > BINGO FUEL.
/// 높은 경고가 생기면 낮은 경고를 끊고 들어가며, 조건이 지속되면 정해진 간격으로 반복한다.
///
/// 음성 클립은 Resources/Audio/Voice/{pull_up, missile, stall, over_g, altitude, bingo_fuel} 에
/// 두면 자동으로 쓰고, 없으면 경고별로 구분되는 합성 경고음으로 대신한다.
/// GameModeManager 가 런타임에 생성한다.
/// </summary>
public class FlightVoiceWarnings : MonoBehaviour
{
    public enum Warning { PullUp, Missile, Stall, OverG, Altitude, Bingo }

    static readonly string[] ClipNames  = { "pull_up", "missile", "stall", "over_g", "altitude", "bingo_fuel" };
    // 반복 간격 (초) — 0 이면 조건 진입 시 1회 (해제 후 재무장)
    static readonly float[]  RepeatSec  = { 1.2f, 2.6f, 1.6f, 3.5f, 0f, 0f };

    const float PullUpTti      = 5f;     // 지면/해면 충돌까지 예상 시간 (s)
    const float PullUpTtiNear  = 2.5f;   // 항모 접근 중에는 더 늦게 (정상 착함 강하율 허용)
    const float AltitudeFloor  = 150f;   // m
    const float AltitudeRearm  = 250f;
    const float CarrierRadius  = 2500f;  // 항모 접근 판정 수평 반경

    AudioSource _src;
    readonly AudioClip[] _clips = new AudioClip[6];
    readonly bool[]  _active = new bool[6];
    readonly bool[]  _armed  = { true, true, true, true, true, true };
    readonly float[] _next   = new float[6];
    int   _playing = -1;
    float _playingUntil;

    PlayerController _pc;
    OceanSurface     _ocean;
    bool             _oceanSearched;
    float            _scanTimer;
    float            _heightAgl = float.MaxValue;
    readonly RaycastHit[] _hits = new RaycastHit[8];

    /// <summary>현재 말하고 있는 경고 (HUD 캡션 등)</summary>
    public static Warning? Speaking { get; private set; }

    void Awake()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.spatialBlend = 0f;
        _src.priority     = 0;
        _src.volume       = 0.9f;
        _src.playOnAwake  = false;

        for (int i = 0; i < _clips.Length; i++)
            _clips[i] = Resources.Load<AudioClip>("Audio/Voice/" + ClipNames[i]) ?? MakeTone((Warning)i);
    }

    void Update()
    {
        if (_pc == null || !_pc.isLocalPlayer)
        {
            _pc = null;
            foreach (var p in PlayerController.All)
                if (p != null && p.isLocalPlayer) { _pc = p; break; }
        }

        var gm = GameModeManager.Instance;
        bool flying = _pc != null && gm != null && gm.IsFlying && !gm.IsDead
                   && (GLocSystem.Instance == null || !GLocSystem.Instance.Unconscious);
        if (!flying)
        {
            for (int i = 0; i < _active.Length; i++) { _active[i] = false; _armed[i] = true; }
            if (_playing >= 0) { _src.Stop(); _playing = -1; Speaking = null; }
            return;
        }

        Evaluate();
        Dispatch();
    }

    void Evaluate()
    {
        var t = _pc.transform;
        float v  = _pc.CurrentSpeed;
        Vector3 vel = t.forward * v;
        float vy = vel.y;

        _scanTimer -= Time.deltaTime;
        if (_scanTimer <= 0f) { _scanTimer = 0.15f; _heightAgl = MeasureHeight(t.position); }

        bool nearCarrier = NearCarrier(t.position);
        bool airborne    = _heightAgl > 6f && v > 20f;

        // PULL UP: 강하율로 계산한 충돌 시간 + 비행 경로 전방 장애물
        bool pullUp = false;
        if (airborne)
        {
            float limit = nearCarrier ? PullUpTtiNear : PullUpTti;
            if (vy < -3f && _heightAgl < 1200f && _heightAgl / -vy < limit) pullUp = true;
            if (!pullUp && v > 1f && RayHitsObstacle(t.position, vel.normalized, v * limit * 0.8f)) pullUp = true;
        }
        _active[(int)Warning.PullUp] = pullUp;

        var tw = ThreatWarningSystem.Instance;
        _active[(int)Warning.Missile] = tw != null && tw.MissileIncoming;
        _active[(int)Warning.Stall]   = airborne && _pc.StallWarning;
        _active[(int)Warning.OverG]   = GLocSystem.Instance != null && GLocSystem.Instance.Stress > 0.55f;

        // ALTITUDE: 저고도로 내려가는 중 1회 (항모 접근 중 제외), 충분히 올라가면 재무장
        if (_heightAgl > AltitudeRearm) _armed[(int)Warning.Altitude] = true;
        _active[(int)Warning.Altitude] = airborne && !nearCarrier && vy < -1f && _heightAgl < AltitudeFloor;

        _active[(int)Warning.Bingo] = _pc.IsFuelLow && !_pc.IsFuelEmpty;
    }

    void Dispatch()
    {
        float now = Time.time;
        if (_playing >= 0 && (!_src.isPlaying || now >= _playingUntil)) { _playing = -1; Speaking = null; }

        for (int i = 0; i < _active.Length; i++)
        {
            bool once = RepeatSec[i] <= 0f;
            if (!_active[i])
            {
                if (once && i != (int)Warning.Altitude) _armed[i] = true;
                if (_playing == i && !once) { _src.Stop(); _playing = -1; Speaking = null; }
                continue;
            }
            if (once && !_armed[i]) continue;
            if (!once && now < _next[i]) continue;

            // 더 높은(작은 번호) 경고가 말하는 중이면 대기, 낮은 경고는 끊는다
            if (_playing >= 0 && _playing <= i) return;

            Play(i);
            if (once) _armed[i] = false;
            return;
        }
    }

    void Play(int i)
    {
        var clip = _clips[i];
        _src.Stop();
        _src.clip = clip;
        _src.Play();
        _playing      = i;
        _playingUntil = Time.time + clip.length;
        _next[i]      = Time.time + clip.length + RepeatSec[i];
        Speaking      = (Warning)i;
    }

    // ── 고도 / 장애물 ─────────────────────────────────────────────────────────
    float MeasureHeight(Vector3 pos)
    {
        float h = float.MaxValue;
        if (!_oceanSearched) { _ocean = FindAnyObjectByType<OceanSurface>(); _oceanSearched = true; }
        if (_ocean != null) h = pos.y - _ocean.seaLevel;

        int n = Physics.RaycastNonAlloc(pos, Vector3.down, _hits, 3000f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.IsChildOf(_pc.transform)) continue;
            h = Mathf.Min(h, _hits[i].distance);
        }
        return h;
    }

    bool RayHitsObstacle(Vector3 pos, Vector3 dir, float range)
    {
        int n = Physics.RaycastNonAlloc(pos, dir, _hits, range,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var carrier = ArrestingWireSystem.Instance != null ? ArrestingWireSystem.Instance.transform.parent : null;
        for (int i = 0; i < n; i++)
        {
            var tr = _hits[i].transform;
            if (tr.IsChildOf(_pc.transform)) continue;
            if (carrier != null && tr.IsChildOf(carrier)) continue;   // 착함 접근은 PULL UP 대상 아님
            if (tr.GetComponentInParent<PlayerController>() != null) continue;   // 다른 기체
            return true;
        }
        return false;
    }

    static bool NearCarrier(Vector3 pos)
    {
        var w = ArrestingWireSystem.Instance;
        if (w == null) return false;
        Vector3 d = pos - w.transform.position; d.y = 0f;
        return d.sqrMagnitude < CarrierRadius * CarrierRadius;
    }

    // ── 합성 경고음 (음성 클립이 없을 때) ────────────────────────────────────
    static AudioClip MakeTone(Warning w)
    {
        const int SR = 22050;
        // (주파수 Hz, 길이 s, 쉼 s) 펄스 열
        float[,] p = w switch
        {
            Warning.PullUp   => new float[,] { { 1250, 0.09f, 0.05f }, { 1250, 0.09f, 0.05f }, { 1250, 0.09f, 0.05f }, { 1250, 0.09f, 0f } },
            Warning.Missile  => new float[,] { { 900, 0.12f, 0.04f }, { 1500, 0.12f, 0f } },
            Warning.Stall    => new float[,] { { 420, 0.22f, 0.08f }, { 420, 0.22f, 0f } },
            Warning.OverG    => new float[,] { { 760, 0.35f, 0f } },
            Warning.Altitude => new float[,] { { 980, 0.18f, 0.06f }, { 740, 0.28f, 0f } },
            _                => new float[,] { { 600, 0.15f, 0.1f }, { 600, 0.15f, 0.1f }, { 600, 0.15f, 0f } },
        };
        float total = 0f;
        for (int i = 0; i < p.GetLength(0); i++) total += p[i, 1] + p[i, 2];
        int len = Mathf.CeilToInt(total * SR);
        var data = new float[len];
        int k = 0;
        for (int i = 0; i < p.GetLength(0); i++)
        {
            int on = (int)(p[i, 1] * SR), off = (int)(p[i, 2] * SR);
            for (int s = 0; s < on && k < len; s++, k++)
            {
                float env = Mathf.Min(1f, Mathf.Min(s, on - s) / (SR * 0.008f));
                float ph  = 2f * Mathf.PI * p[i, 0] * s / SR;
                data[k] = env * 0.45f * (Mathf.Sin(ph) + 0.3f * Mathf.Sin(ph * 2f));
            }
            k += off;
        }
        var clip = AudioClip.Create("voice_" + w, len, 1, SR, false);
        clip.SetData(data, 0);
        return clip;
    }
}
