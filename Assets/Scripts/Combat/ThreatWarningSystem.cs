using System.Collections.Generic;
using UnityEngine;

public class ThreatWarningSystem : MonoBehaviour
{
    public static ThreatWarningSystem Instance { get; private set; }

    public enum ThreatLevel { None, Detected, Tracked, Locked, MissileFired, MissileActive }

    public ThreatLevel Threat          { get; private set; }
    public float       ThreatBearing   { get; private set; }
    public bool        MissileIncoming { get; private set; }
    public float       NearestMissileDist { get; private set; } = float.MaxValue;

    PlayerController _local;
    AudioSource      _audio;
    AudioClip        _lockBeep;
    AudioClip        _missileBeep;
    AudioClip        _arhBeep;         // ARH 시커 활성 전용 경보음
    float            _scanTimer;
    float            _audioTimer;
    bool             _missileBeepToggle;

    // ── 네트워크 위협 (MISSILE_WARN) ─────────────────────────────────────
    // 발신자별로 단계와 만료 시각을 보관 — 갱신이 끊기면(락 해제·미사일 기만) 잠시 뒤 RWR 에서 사라진다.
    // 방위는 매 스캔 발신 기체의 현재 위치로 다시 계산한다.
    struct NetThreat { public ThreatLevel level; public float expires; public Vector3 lastPos; }
    readonly Dictionary<string, NetThreat> _netThreats = new();
    readonly List<string> _expired = new();
    const float NetLockHold    = 0.9f;   // 락 경보 갱신 주기(0.3~0.4s) × 2 이상
    const float NetMissileHold = 0.8f;
    const string MissileKeySuffix = "#M";

    /// <summary>원격(또는 호스트의 AI)이 우리를 탐지·락온·미사일 유도 중</summary>
    public void ReportNetworkThreat(string shooter, ThreatLevel level, Vector3 shooterPos)
    {
        if (string.IsNullOrEmpty(shooter) || level == ThreatLevel.None) return;
        // 레이더 락(발사 기체)과 미사일 경보는 따로 만료된다 — 미사일이 기만돼도 락 경보는 남는다
        bool  msl  = level >= ThreatLevel.MissileFired;
        string key = msl ? shooter + MissileKeySuffix : shooter;
        if (_netThreats.TryGetValue(key, out var cur) && cur.level > level && cur.expires > Time.time)
            level = cur.level;
        _netThreats[key] = new NetThreat { level = level, expires = Time.time + (msl ? NetMissileHold : NetLockHold), lastPos = shooterPos };
    }

    /// <summary>구형 호출 호환</summary>
    public void ReportNetworkThreat(ThreatLevel level, Vector3 threatWorldPos)
        => ReportNetworkThreat("?", level, threatWorldPos);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _audio              = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 0f;
        _audio.volume       = 0.72f;
        _audio.priority     = 0;    // 가장 높은 우선순위

        // 절차적 경고음 생성 (오디오 파일 없이 코드로 직접 PCM 생성)
        _lockBeep    = MakeBeep(880f,  0.08f);                    // 락온: 880 Hz 짧은 비프
        _missileBeep = MakeDualBeep(1320f, 680f, 0.055f, 0.04f); // 미사일: 고저 교번 긴급음
        _arhBeep     = MakeDualBeep(1760f, 1320f, 0.04f, 0.03f); // ARH 시커 활성: 고주파 연속음
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() => FindLocal();

    void FindLocal()
    {
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
    }

    void Update()
    {
        if (_local == null) { FindLocal(); return; }

        _scanTimer += Time.deltaTime;
        if (_scanTimer >= 0.10f) { _scanTimer = 0f; ScanThreats(); }

        PlayWarningAudio();
    }

    void ScanThreats()
    {
        ThreatLevel worst   = ThreatLevel.None;
        float       bearing = 0f;
        bool        missile = false;
        float       minDist = float.MaxValue;
        Vector3     me      = _local.transform.position;
        string      myNick  = _local.nickname;

        // 1) 이 클라이언트가 시뮬레이션 중인 미사일 (호스트: AI 가 나를 향해 쏜 것)
        foreach (var mc in MissileController.All)
        {
            if (mc == null || mc.Spec == null || !mc.IsTrackingTarget || mc.IntendedTarget != _local.transform) continue;
            ConsiderMissile(mc.transform.position, mc.IsARHActive || mc.Spec.IsIR, ref worst, ref bearing, ref missile, ref minDist, me);
        }

        // 2) 원격 미사일 뷰 — 나를 표적으로 발사되었고 아직 나를 향해 날아오는 것 (기만된 미사일은 빗나가며 방향이 틀어진다)
        foreach (var rv in RemoteMissileView.All)
        {
            if (rv == null || rv.TargetNick != myNick) continue;
            Vector3 to = me - rv.transform.position;
            if (Vector3.Angle(rv.transform.forward, to) > 70f) continue;
            bool active = _netThreats.TryGetValue(rv.ShooterNick + MissileKeySuffix, out var nt) && nt.expires > Time.time
                        && nt.level >= ThreatLevel.MissileFired;
            if (!active) continue;
            ConsiderMissile(rv.transform.position, nt.level >= ThreatLevel.MissileActive, ref worst, ref bearing, ref missile, ref minDist, me);
        }

        // 3) 네트워크 경보 (탐지·락·미사일 — 미사일 오브젝트를 못 본 경우 포함)
        _expired.Clear();
        ThreatLevel netBest = ThreatLevel.None;
        Vector3     netSrc  = Vector3.zero;
        foreach (var kv in _netThreats)
        {
            if (kv.Value.expires <= Time.time) { _expired.Add(kv.Key); continue; }
            var lv = kv.Value.level;
            if (lv >= ThreatLevel.MissileFired) missile = true;
            if (lv > worst) worst = lv;
            if (lv > netBest) { netBest = lv; netSrc = ShooterPosition(kv.Key, kv.Value.lastPos); }
        }
        // 미사일 위치를 모르면 가장 위험한 발신 기체 방향을 가리킨다
        if (minDist == float.MaxValue && netBest > ThreatLevel.None && netSrc != Vector3.zero)
            bearing = CalcBearing(netSrc - me);
        foreach (var k in _expired) _netThreats.Remove(k);

        Threat              = worst;
        ThreatBearing       = bearing;
        MissileIncoming     = missile;
        NearestMissileDist  = minDist;
    }

    void ConsiderMissile(Vector3 pos, bool active, ref ThreatLevel worst, ref float bearing,
                         ref bool missile, ref float minDist, Vector3 me)
    {
        missile = true;
        float d = Vector3.Distance(pos, me);
        if (d < minDist) { minDist = d; bearing = CalcBearing(pos - me); }
        var lv = active ? ThreatLevel.MissileActive : ThreatLevel.MissileFired;
        if (lv > worst) worst = lv;
    }

    static Vector3 ShooterPosition(string key, Vector3 fallback)
    {
        string nick = key.EndsWith(MissileKeySuffix) ? key[..^MissileKeySuffix.Length] : key;
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.nickname == nick) return pc.transform.position;
        return fallback;
    }

    void PlayWarningAudio()
    {
        if (_audio == null) return;
        _audioTimer -= Time.deltaTime;
        if (_audioTimer > 0f) return;

        if (Threat >= ThreatLevel.MissileActive && MissileIncoming)
        {
            // ARH 시커 활성: 고주파 급속 경보 (0.10초 간격)
            if (_arhBeep != null) _audio.PlayOneShot(_arhBeep, 1.0f);
            _audioTimer = 0.10f;
        }
        else if (MissileIncoming)
        {
            // 고저 교번 긴급 경고음 (0.15초 간격으로 빠르게)
            if (_missileBeep != null) _audio.PlayOneShot(_missileBeep, 1.0f);
            _audioTimer = 0.15f;
        }
        else if (Threat >= ThreatLevel.Locked)
        {
            if (_lockBeep != null) _audio.PlayOneShot(_lockBeep, 0.80f);
            _audioTimer = 0.42f;
        }
        else if (Threat == ThreatLevel.Tracked)
        {
            if (_lockBeep != null) _audio.PlayOneShot(_lockBeep, 0.40f);
            _audioTimer = 0.90f;
        }
    }

    float CalcBearing(Vector3 dir)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(_local.transform.forward, Vector3.up);
        Vector3 d   = Vector3.ProjectOnPlane(dir, Vector3.up);
        return fwd.sqrMagnitude < 0.001f ? 0f
             : Vector3.SignedAngle(fwd, d, Vector3.up);
    }

    // ── 절차적 PCM 비프음 생성 ────────────────────────────────────────────
    static AudioClip MakeBeep(float freq, float duration, int sr = 22050)
    {
        int     n   = Mathf.Max(1, Mathf.CeilToInt(sr * duration));
        float[] buf = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / sr;
            float env = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((duration - t) / 0.006f);
            buf[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.65f;
        }
        var clip = AudioClip.Create($"Beep{freq:F0}", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 고음 → 저음 두 톤 연속 (미사일 경보 전용)
    static AudioClip MakeDualBeep(float freqHi, float freqLo, float durHi, float durLo, int sr = 22050)
    {
        int splitN = Mathf.Max(1, Mathf.CeilToInt(sr * durHi));
        int totalN = splitN + Mathf.Max(1, Mathf.CeilToInt(sr * durLo));
        float[] buf = new float[totalN];

        for (int i = 0; i < totalN; i++)
        {
            bool  hi   = i < splitN;
            float freq = hi ? freqHi : freqLo;
            float dur  = hi ? durHi  : durLo;
            float t    = hi ? (float)i / sr : (float)(i - splitN) / sr;
            float env  = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((dur - t) / 0.005f);
            buf[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.78f;
        }
        var clip = AudioClip.Create("MissileWarn", totalN, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }
}
