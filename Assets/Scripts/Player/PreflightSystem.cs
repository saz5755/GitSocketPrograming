using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public enum PreflightStage
{
    None,
    Waiting,         // 존 내 대기 — F키로 시작
    ApuStarting,     // APU 스핀업 중 (1.5초)
    ApuReady,        // APU 완료 — 엔진 시동 가능
    EngineStarting,  // 엔진 점화 중 (3초)
    EngineIdle,      // 엔진 IDLE — 항전 투입 가능
    AvionicsOn,      // 항전 활성화 (1.5초)
    ReadyForLaunch   // 출격 허가
}

/// <summary>
/// 갑판 대기 → APU → 엔진 → 항전 → READY FOR LAUNCH 순서를 관리하는 상태 머신.
/// GameModeManager.Init()에서 AddComponent로 생성.
/// </summary>
public class PreflightSystem : MonoBehaviour
{
    public static PreflightSystem Instance { get; private set; }

    public PreflightStage Stage { get; private set; } = PreflightStage.None;
    public bool IsActive         => Stage != PreflightStage.None && Stage != PreflightStage.ReadyForLaunch;
    public bool IsReadyForLaunch => Stage == PreflightStage.ReadyForLaunch;

    // ── UI refs ──────────────────────────────────────────────────────────────
    Canvas    _panel;
    Text      _titleText;
    Text      _instrText;
    Image     _progressFill;
    Text      _apuInd, _engInd, _avInd;

    // 플래시 배너 (READY FOR LAUNCH)
    Canvas    _bannerCanvas;
    Text      _bannerText;
    Coroutine _bannerRoutine;

    // ── Audio ─────────────────────────────────────────────────────────────────
    AudioSource _apuSrc;
    AudioSource _engSrc;

    // ── NPC ref ──────────────────────────────────────────────────────────────
    LaunchOfficerNPC _officer;
    F35VFXController _vfx;

    Coroutine _seqRoutine;
    float     _stepProgress;
    bool      _stepReady;

    const float BAR_W  = 420f;
    const float PANEL_W = 520f;
    const float PANEL_H = 230f;

    static readonly Color HG   = new Color(0.00f, 0.98f, 0.42f, 1.00f);
    static readonly Color WARN = new Color(1.00f, 0.82f, 0.00f, 1.00f);
    static readonly Color DIM  = new Color(0.45f, 0.45f, 0.45f, 1.00f);
    static readonly Color BGC  = new Color(0.00f, 0.03f, 0.07f, 0.90f);

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Initialize(LaunchOfficerNPC officer = null)
    {
        _officer = officer;
        BuildUI();
        BuildBanner();
        BuildAudio();
    }

    // ── 외부 API ──────────────────────────────────────────────────────────────
    public void BeginPreflight() => BeginCockpitPreflight(null);

    public void BeginCockpitPreflight(F35VFXController vfx)
    {
        if (Stage != PreflightStage.None) return;
        _vfx        = vfx;
        _stepReady  = false;
        _seqRoutine = StartCoroutine(Sequence());
    }

    // 스위치 클릭 이벤트 — CockpitInteractionSystem이 호출
    public void OnSwitchClicked(int idx)
    {
        PreflightStage[] needed =
        { PreflightStage.Waiting, PreflightStage.ApuReady, PreflightStage.EngineIdle };
        if (idx >= 0 && idx < 3 && Stage == needed[idx])
            _stepReady = true;
    }

    public void ResetPreflight()
    {
        if (_seqRoutine   != null) { StopCoroutine(_seqRoutine);   _seqRoutine   = null; }
        if (_bannerRoutine != null) { StopCoroutine(_bannerRoutine); _bannerRoutine = null; }
        _apuSrc?.Stop();
        _engSrc?.Stop();
        _vfx?.SetEngineIdle(false);
        _vfx       = null;
        _stepReady = false;
        Stage      = PreflightStage.None;
        ShowPanel(false);
        if (_bannerCanvas != null) _bannerCanvas.gameObject.SetActive(false);
        ResetIndicators();
        ResetSwitchStates();
    }

    static void ResetSwitchStates()
    {
        for (int i = 0; i < 3; i++) CockpitSwitch.Get(i)?.SetState(CockpitSwitch.State.Locked);
    }

    // ─────────────────────────────────────────────────────────────────────────
    IEnumerator Sequence()
    {
        Stage = PreflightStage.Waiting;
        ShowPanel(true);
        ResetIndicators();
        CockpitSwitch.Get(0)?.SetState(CockpitSwitch.State.Available);
        CockpitSwitch.Get(1)?.SetState(CockpitSwitch.State.Locked);
        CockpitSwitch.Get(2)?.SetState(CockpitSwitch.State.Locked);

        // ── STEP 1: APU ──────────────────────────────────────────────────────
        SetTitle("  APU START  ", WARN);
        SetInstr("Click APU lever (left console)");
        yield return WaitClick();

        CockpitSwitch.Get(0)?.SetState(CockpitSwitch.State.Done);
        Stage = PreflightStage.ApuStarting;
        SetTitle("APU STARTING...", WARN);
        SetInstr("High-pressure turbine spinning up");
        _apuSrc?.Play();
        yield return RunBar(1.5f);
        _apuSrc?.Stop();

        Stage = PreflightStage.ApuReady;
        SetInd(_apuInd, true);
        CockpitSwitch.Get(1)?.SetState(CockpitSwitch.State.Available);
        SetTitle("  APU READY  ", HG);
        SetInstr("Click ENGINE lever (center console)");
        _stepProgress = 0f; UpdateBar();
        yield return WaitClick();

        // ── STEP 2: ENGINE ───────────────────────────────────────────────────
        CockpitSwitch.Get(1)?.SetState(CockpitSwitch.State.Done);
        Stage = PreflightStage.EngineStarting;
        SetTitle("ENGINE STARTING...", WARN);
        SetInstr("N1 RPM rising — ignition sequence");
        _engSrc?.Play();
        yield return RunBar(3f);
        _engSrc?.Stop();

        Stage = PreflightStage.EngineIdle;
        SetInd(_engInd, true);
        _vfx?.SetEngineIdle(true);           // 엔진 배기열 VFX 활성화
        CockpitSwitch.Get(2)?.SetState(CockpitSwitch.State.Available);
        SetTitle(" ENGINE IDLE ", HG);
        SetInstr("Click AVIONICS switch (right console)");
        _stepProgress = 0f; UpdateBar();
        yield return WaitClick();

        // ── STEP 3: AVIONICS ─────────────────────────────────────────────────
        CockpitSwitch.Get(2)?.SetState(CockpitSwitch.State.Done);
        Stage = PreflightStage.AvionicsOn;
        SetTitle("AVIONICS POWER ON...", WARN);
        SetInstr("MFD & HUD initializing");
        yield return RunBar(1.5f);
        SetInd(_avInd, true);

        // ── READY FOR LAUNCH ─────────────────────────────────────────────────
        Stage = PreflightStage.ReadyForLaunch;
        SetTitle("■ READY FOR LAUNCH ■", HG);
        SetInstr("[F] — Catapult launch");
        if (_titleText != null) _titleText.fontSize = 28;
        _stepProgress = 1f; UpdateBar();

        if (_officer == null) _officer = LaunchOfficerNPC.Instance;
        _officer?.TriggerLaunchSignal();
        _bannerRoutine = StartCoroutine(ShowBanner());

        _seqRoutine = null;
    }

    IEnumerator WaitClick()
    {
        _stepReady = false;
        while (!_stepReady)
            yield return null;
    }

    IEnumerator RunBar(float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t            += Time.deltaTime;
            _stepProgress =  Mathf.Clamp01(t / dur);
            UpdateBar();
            yield return null;
        }
        _stepProgress = 1f;
        UpdateBar();
    }

    // ── READY FOR LAUNCH 플래시 배너 ─────────────────────────────────────────
    IEnumerator ShowBanner()
    {
        if (_bannerCanvas == null) yield break;
        _bannerCanvas.gameObject.SetActive(true);

        // 6회 깜박임 (0.25초 간격)
        for (int i = 0; i < 6; i++)
        {
            _bannerText.enabled = !_bannerText.enabled;
            yield return new WaitForSeconds(0.25f);
        }
        _bannerText.enabled = true;

        // 4초 표시 후 서서히 사라짐
        yield return new WaitForSeconds(4f);
        float alpha = 1f;
        while (alpha > 0f)
        {
            alpha -= Time.deltaTime * 0.7f;
            _bannerText.color = new Color(HG.r, HG.g, HG.b, alpha);
            yield return null;
        }
        _bannerCanvas.gameObject.SetActive(false);
        _bannerRoutine = null;
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var root = new GameObject("PreflightPanel");
        _panel = root.AddComponent<Canvas>();
        _panel.renderMode   = RenderMode.ScreenSpaceOverlay;
        _panel.sortingOrder = 52;
        root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        // 배경
        var bg = MakeRT("BG", root.transform);
        bg.anchorMin        = new Vector2(0.5f, 0f);
        bg.anchorMax        = new Vector2(0.5f, 0f);
        bg.pivot            = new Vector2(0.5f, 0f);
        bg.anchoredPosition = new Vector2(0f, 110f);
        bg.sizeDelta        = new Vector2(PANEL_W, PANEL_H);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = BGC;

        // 외곽선
        var border = MakeRT("Border", root.transform);
        border.anchorMin        = new Vector2(0.5f, 0f);
        border.anchorMax        = new Vector2(0.5f, 0f);
        border.pivot            = new Vector2(0.5f, 0f);
        border.anchoredPosition = new Vector2(0f, 108f);
        border.sizeDelta        = new Vector2(PANEL_W + 4f, PANEL_H + 4f);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.color = new Color(0f, 0.6f, 0.3f, 0.6f);
        border.SetAsFirstSibling(); // 배경 뒤에

        var fnt = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 헤더 (패널 상단)
        MakeLabel("── PREFLIGHT SEQUENCE ──", bg, new Vector2(0f, 96f), 13,
                  new Color(0.3f, 0.7f, 1f, 0.9f), fnt);

        // 타이틀 (현재 단계)
        _titleText = MakeLabel("APU START", bg, new Vector2(0f, 63f), 26, WARN, fnt);

        // 지시 텍스트
        _instrText = MakeLabel("[E] — Start Auxiliary Power Unit", bg,
                               new Vector2(0f, 28f), 16,
                               new Color(0.85f, 0.92f, 1f, 0.9f), fnt);

        // 인디케이터 행 (APU / ENG / AV)
        _apuInd = MakeLabel("● APU", bg, new Vector2(-155f, -8f), 15, DIM, fnt);
        _engInd = MakeLabel("● ENG", bg, new Vector2(   0f, -8f), 15, DIM, fnt);
        _avInd  = MakeLabel("●  AV", bg, new Vector2( 155f, -8f), 15, DIM, fnt);

        // 구분선
        var sep = MakeRT("Sep", bg);
        sep.anchoredPosition = new Vector2(0f, -30f);
        sep.sizeDelta        = new Vector2(460f, 1f);
        sep.gameObject.AddComponent<Image>().color = new Color(0f, 0.6f, 0.3f, 0.4f);

        // 프로그레스 바 배경
        var barBg = MakeRT("BarBg", bg);
        barBg.anchoredPosition = new Vector2(0f, -52f);
        barBg.sizeDelta        = new Vector2(BAR_W, 14f);
        barBg.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.10f, 0.05f, 1f);

        // 프로그레스 바 채우기 (왼쪽 고정, 너비로 진행률 표현)
        var fill = MakeRT("Fill", barBg);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot     = new Vector2(0f, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = Vector2.zero;
        _progressFill  = fill.gameObject.AddComponent<Image>();
        _progressFill.color = HG;

        root.SetActive(false);
    }

    void BuildBanner()
    {
        var go = new GameObject("ReadyForLaunchBanner");
        _bannerCanvas = go.AddComponent<Canvas>();
        _bannerCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _bannerCanvas.sortingOrder = 58;

        var rt = MakeRT("Text", go.transform);
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 160f);
        rt.sizeDelta        = new Vector2(900f, 90f);

        _bannerText = rt.gameObject.AddComponent<Text>();
        _bannerText.text      = "■ READY FOR LAUNCH ■";
        _bannerText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _bannerText.fontSize  = 52;
        _bannerText.fontStyle = FontStyle.Bold;
        _bannerText.color     = HG;
        _bannerText.alignment = TextAnchor.MiddleCenter;

        go.SetActive(false);
    }

    // ── 오디오 빌드 ───────────────────────────────────────────────────────────
    void BuildAudio()
    {
        const int SR = 22050;

        _apuSrc = gameObject.AddComponent<AudioSource>();
        _apuSrc.clip         = MakeAPUClip(SR);
        _apuSrc.loop         = false;
        _apuSrc.volume       = 0.55f;
        _apuSrc.spatialBlend = 0f;

        _engSrc = gameObject.AddComponent<AudioSource>();
        _engSrc.clip         = MakeEngineStartClip(SR);
        _engSrc.loop         = false;
        _engSrc.volume       = 0.75f;
        _engSrc.spatialBlend = 0f;
    }

    // APU 스핀업: 고주파 와이어 회전음 (3000→7500 Hz 상승 톤 + 노이즈)
    static AudioClip MakeAPUClip(int sr)
    {
        int     n   = Mathf.RoundToInt(sr * 2f);
        float[] d   = new float[n];
        var     rng = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t     = (float)i / sr;
            float ramp  = Mathf.Clamp01(t / 1.5f);
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.25f;
            float freq  = Mathf.Lerp(2800f, 7200f, ramp);
            float tone  = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.55f;
            // 고조파 (2배음)
            float harm  = Mathf.Sin(4f * Mathf.PI * freq * t) * 0.20f;
            d[i] = Mathf.Clamp((noise + tone + harm) * ramp, -1f, 1f);
        }
        var clip = AudioClip.Create("APU_Start", n, 1, sr, false);
        clip.SetData(d, 0);
        return clip;
    }

    // 엔진 시동: 저주파 럼블 → 제트 점화음 상승
    static AudioClip MakeEngineStartClip(int sr)
    {
        int     n   = Mathf.RoundToInt(sr * 3.8f);
        float[] d   = new float[n];
        var     rng = new System.Random(31);
        for (int i = 0; i < n; i++)
        {
            float t     = (float)i / sr;
            float ramp  = Mathf.Clamp01(t / 3f);
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.22f;
            // N1 RPM 상승 럼블 (80→200 Hz)
            float rpm   = Mathf.Sin(2f * Mathf.PI * (80f + ramp * 120f) * t) * 0.40f;
            // 점화 후 제트음 (0.4초 이후 등장)
            float jet   = t > 0.4f
                ? Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(180f, 1400f, (t - 0.4f) / 2.8f) * t) * 0.30f * Mathf.Clamp01((t - 0.4f) / 1.5f)
                : 0f;
            // 고주파 터빈음 (3000 Hz 소량)
            float turbine = Mathf.Sin(2f * Mathf.PI * 3200f * t) * 0.08f * ramp;
            d[i] = Mathf.Clamp(noise + rpm + jet + turbine, -1f, 1f);
        }
        var clip = AudioClip.Create("Eng_Start", n, 1, sr, false);
        clip.SetData(d, 0);
        return clip;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────
    void ShowPanel(bool show)
    {
        if (_panel != null) _panel.gameObject.SetActive(show);
    }

    void SetTitle(string msg, Color col)
    {
        if (_titleText == null) return;
        _titleText.text     = msg;
        _titleText.color    = col;
        _titleText.fontSize = 26;
        _stepProgress = 0f;
        UpdateBar();
    }

    void SetInstr(string msg)
    {
        if (_instrText != null) _instrText.text = msg;
    }

    void SetInd(Text t, bool on)
    {
        if (t == null) return;
        t.color = on ? HG : DIM;
    }

    void ResetIndicators()
    {
        SetInd(_apuInd, false);
        SetInd(_engInd, false);
        SetInd(_avInd,  false);
        if (_titleText != null) _titleText.fontSize = 26;
        _stepProgress = 0f;
        UpdateBar();
    }

    void UpdateBar()
    {
        if (_progressFill == null) return;
        _progressFill.rectTransform.sizeDelta = new Vector2(BAR_W * _stepProgress, 0f);
    }

    static RectTransform MakeRT(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        return rt;
    }

    static Text MakeLabel(string content, RectTransform parent, Vector2 apos,
                           int fontSize, Color color, Font fnt)
    {
        var rt       = MakeRT(content.Length > 16 ? content[..16] : content, parent);
        rt.anchoredPosition = apos;
        rt.sizeDelta        = new Vector2(PANEL_W - 20f, 44f);
        var txt = rt.gameObject.AddComponent<Text>();
        txt.font      = fnt;
        txt.text      = content;
        txt.fontSize  = fontSize;
        txt.color     = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.fontStyle = FontStyle.Bold;
        return txt;
    }
}
