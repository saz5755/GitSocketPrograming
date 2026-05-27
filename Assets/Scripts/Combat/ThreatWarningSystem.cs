using System.Collections.Generic;
using UnityEngine;

public class ThreatWarningSystem : MonoBehaviour
{
    public enum ThreatLevel { None, Detected, Tracked, Locked, MissileFired, MissileActive }

    public ThreatLevel Threat          { get; private set; }
    public float       ThreatBearing   { get; private set; }
    public bool        MissileIncoming { get; private set; }
    // 가장 가까운 인입 미사일까지의 거리
    public float       NearestMissileDist { get; private set; } = float.MaxValue;

    PlayerController _local;
    AudioSource      _audio;
    AudioClip        _lockBeep;
    AudioClip        _missileBeep;
    AudioClip        _arhBeep;         // ARH 시커 활성 전용 경보음
    float            _scanTimer;
    float            _audioTimer;
    bool             _missileBeepToggle;
    float            _networkMissileTimer;   // 네트워크 미사일 경고 잔류 시간

    // 네트워크에서 호출: 원격 플레이어가 우리를 락온/미사일 발사 중
    public void ReportNetworkThreat(ThreatLevel level, Vector3 threatWorldPos)
    {
        if (level > Threat) Threat = level;
        // MissileFired 수신 시 0.65초 동안 MissileIncoming 유지
        if (level >= ThreatLevel.MissileFired) _networkMissileTimer = 0.65f;
        if (_local != null)
            ThreatBearing = CalcBearing(threatWorldPos - _local.transform.position);
    }

    void Awake()
    {
        _audio              = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 0f;
        _audio.volume       = 0.72f;
        _audio.priority     = 0;    // 가장 높은 우선순위

        // 절차적 경고음 생성 (오디오 파일 없이 코드로 직접 PCM 생성)
        _lockBeep    = MakeBeep(880f,  0.08f);                    // 락온: 880 Hz 짧은 비프
        _missileBeep = MakeDualBeep(1320f, 680f, 0.055f, 0.04f); // 미사일: 고저 교번 긴급음
        _arhBeep     = MakeDualBeep(1760f, 1320f, 0.04f, 0.03f); // ARH 시커 활성: 고주파 연속음
    }

    void Start() => FindLocal();

    void FindLocal()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>())
            if (pc.isLocalPlayer) { _local = pc; break; }
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

        // 인입 미사일 탐지 (로컬 씬의 MissileController – 발사자 클라이언트에서 실행 중)
        foreach (var mc in FindObjectsOfType<MissileController>())
        {
            if (mc.Target != _local.transform) continue;
            missile = true;
            float dist = Vector3.Distance(mc.transform.position, _local.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                bearing = CalcBearing(mc.transform.position - _local.transform.position);
            }
            if (ThreatLevel.MissileFired > worst) worst = ThreatLevel.MissileFired;
        }

        // 네트워크 미사일 타이머 (원격 발사 – 씬에 오브젝트 없음)
        if (_networkMissileTimer > 0f)
        {
            _networkMissileTimer -= 0.10f;  // ScanThreats 0.1초 간격과 동기
            missile = true;
            if (ThreatLevel.MissileFired > worst) worst = ThreatLevel.MissileFired;
        }

        // 로컬 타겟팅 시스템 락온 경고 (미사일이 없을 때)
        if (!missile)
        {
            foreach (var ts in FindObjectsOfType<TargetingSystem>())
            {
                if (ts.LocalPlayer == _local || ts.Target != _local) continue;
                ThreatLevel lv = ts.State == TargetingSystem.LockState.Locked    ? ThreatLevel.Locked
                               : ts.State == TargetingSystem.LockState.Searching ? ThreatLevel.Tracked
                               : ThreatLevel.None;
                if (lv > worst && ts.LocalPlayer != null)
                {
                    worst   = lv;
                    bearing = CalcBearing(ts.LocalPlayer.transform.position - _local.transform.position);
                }
            }
        }

        Threat              = worst;
        ThreatBearing       = bearing;
        MissileIncoming     = missile;
        NearestMissileDist  = missile ? minDist : float.MaxValue;
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
