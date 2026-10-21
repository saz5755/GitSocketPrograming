using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로그인 화면의 사이버펑크 "디지털 전장 진입" 레이어:
///  · 상시 HUD 오버레이 — 스캔라인, 도트 그리드, 코너 브래킷, 텔레메트리(UTC/좌표/노드), 가끔 지나가는 글리치 라인, 전투기를 조준하는 타깃 레티클
///  · 인증 연출 — 폼이 업링크 터미널로 바뀌며 부팅 로그가 타이핑되고, 레티클이 전투기에 LOCK 된다
///  · 성공 시 ACCESS GRANTED → 글리치 와이프로 로비 진입
/// </summary>
public class LoginAuthFX : MonoBehaviour
{
    // ── 공개 상태 ─────────────────────────────────────────────────────────
    public bool Playing { get; private set; }
    public bool Granted { get; private set; }
    public float Elapsed { get; private set; }
    /// <summary>ACCESS GRANTED 가 표시된 뒤 경과 시간 (아직이면 -1)</summary>
    public float GrantedFor => _grantedT;

    static readonly Color Cyan = new Color(0.30f, 0.86f, 1f, 1f);
    static readonly Color Amber = new Color(1f, 0.62f, 0.26f, 1f);
    static readonly Color Ink = new Color(0.86f, 0.97f, 1f, 1f);

    Font _font;
    UITheme _t;
    RectTransform _overlay, _form;
    CanvasGroup _hudGroup;
    RawImage _scan, _dots;
    RectTransform _spinner;

    // 레티클
    RectTransform _ret, _retDash, _retRing, _retBox;
    Text _retTag, _retRange, _retLock;
    Image[] _retCorners = new Image[4];
    Image _lockFlash;

    // 텔레메트리
    Text _utc, _node;

    // 터미널
    CanvasGroup _term;
    class Line { public Text left, right; public string full; public string status; public Color statusColor; public float start; public float statusAt; public bool shown; }
    readonly System.Collections.Generic.List<Line> _lines = new System.Collections.Generic.List<Line>();
    Image[] _barSegs = new Image[26];
    Text _grantedText, _caret;
    string _pilot = "PILOT", _server = "";

    // 글리치
    float _nextGlitch = 3f;
    RectTransform[] _glitchBars = new RectTransform[3];
    float _glitchT = -1f;

    float _grantedT = -1f;
    float _fadeIn;
    static Sprite _spinSprite, _dashSprite;

    // ── 생성 ──────────────────────────────────────────────────────────────
    public static LoginAuthFX Create(Transform canvasRoot, RectTransform heroRoot, RectTransform form, RectTransform button)
    {
        var go = new GameObject("LoginAuthFX", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        var fx = go.AddComponent<LoginAuthFX>();
        fx.Build(canvasRoot, heroRoot, form, button);
        return fx;
    }

    void Build(Transform canvasRoot, RectTransform hero, RectTransform form, RectTransform button)
    {
        _t = UITheme.Instance;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _form = form;

        BuildHud(canvasRoot, hero);
        if (button != null) BuildSpinner(button);
        if (form != null) BuildTerminal(form);
    }

    // ── 상시 HUD 오버레이 ──────────────────────────────────────────────────
    void BuildHud(Transform canvasRoot, RectTransform hero)
    {
        var go = new GameObject("CyberHud", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        _overlay = (RectTransform)go.transform;
        _overlay.anchorMin = Vector2.zero; _overlay.anchorMax = Vector2.one; _overlay.offsetMin = _overlay.offsetMax = Vector2.zero;
        if (hero != null) _overlay.SetSiblingIndex(hero.GetSiblingIndex() + 1);
        _hudGroup = go.AddComponent<CanvasGroup>(); _hudGroup.alpha = 0f; _hudGroup.blocksRaycasts = false; _hudGroup.interactable = false;

        // 코너 브래킷
        const float M = 34f, L = 46f;
        Bracket(M, M, L, 1, 1); Bracket(M, M, L, -1, 1); Bracket(M, M, L, 1, -1); Bracket(M, M, L, -1, -1);

        // 상단 좌/우, 하단 텔레메트리
        Txt(_overlay, UIType.Track("TACTICAL UPLINK", 1), Anchor(0f, 1f), new Vector2(M + 56f, -M - 8f), new Vector2(420f, 18f), 11, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0f, 0.5f));
        Txt(_overlay, "KF-21 // v0.1.0", Anchor(0f, 1f), new Vector2(M + 56f, -M - 26f), new Vector2(420f, 16f), 10, new Color(Ink.r, Ink.g, Ink.b, 0.5f), TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0f, 0.5f));
        _node = Txt(_overlay, "NODE --", Anchor(1f, 1f), new Vector2(-M - 150f, -M - 8f), new Vector2(420f, 18f), 11, Cyan, TextAnchor.MiddleRight, FontStyle.Bold, new Vector2(1f, 0.5f));
        Txt(_overlay, "SYS: ONLINE   ENC: AES-256", Anchor(1f, 1f), new Vector2(-M - 150f, -M - 26f), new Vector2(420f, 16f), 10, new Color(Ink.r, Ink.g, Ink.b, 0.5f), TextAnchor.MiddleRight, FontStyle.Normal, new Vector2(1f, 0.5f));
        _utc = Txt(_overlay, "UTC 00:00:00", Anchor(1f, 0f), new Vector2(-M - 56f, M + 84f), new Vector2(420f, 18f), 12, Ink, TextAnchor.MiddleRight, FontStyle.Bold, new Vector2(1f, 0.5f));
        Txt(_overlay, "N37.5512  E126.9882", Anchor(1f, 0f), new Vector2(-M - 56f, M + 66f), new Vector2(420f, 16f), 10, new Color(Ink.r, Ink.g, Ink.b, 0.5f), TextAnchor.MiddleRight, FontStyle.Normal, new Vector2(1f, 0.5f));

        BuildReticle();

    }

    void BuildReticle()
    {
        _ret = new GameObject("Reticle", typeof(RectTransform)).GetComponent<RectTransform>();
        _ret.SetParent(_overlay, false);
        _ret.anchorMin = _ret.anchorMax = new Vector2(0.60f, 0.50f); _ret.sizeDelta = Vector2.zero;

        // 점선 회전 링
        _retDash = Child(_ret, "Dash", Vector2.zero, new Vector2(420f, 420f));
        var di = _retDash.gameObject.AddComponent<Image>(); di.sprite = DashSprite(); di.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f); di.raycastTarget = false;

        // 얇은 실선 링
        _retRing = Child(_ret, "Ring", Vector2.zero, new Vector2(330f, 330f));
        var ri = _retRing.gameObject.AddComponent<Image>(); UIShapes.Round(ri, 165); ri.color = Color.clear; ri.raycastTarget = false;
        UIShapes.AddBorder(_retRing, 165, new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f));

        // 십자 눈금
        for (int k = 0; k < 4; k++)
        {
            bool h = k % 2 == 0; float s = k < 2 ? -1f : 1f;
            var tk = Child(_ret, "Tick" + k, h ? new Vector2(s * 196f, 0f) : new Vector2(0f, s * 196f), h ? new Vector2(26f, 2f) : new Vector2(2f, 26f));
            tk.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f);
        }
        Child(_ret, "Dot", Vector2.zero, new Vector2(5f, 5f)).gameObject.AddComponent<Image>().color = Amber;

        // 락온 박스(코너 브래킷) — 인증 중 수축
        _retBox = Child(_ret, "Box", Vector2.zero, new Vector2(520f, 330f));
        float bw = 260f, bh = 165f, bl = 34f;
        for (int k = 0; k < 4; k++)
        {
            float sx = (k % 2 == 0) ? -1f : 1f, sy = (k < 2) ? 1f : -1f;
            var hz = Child(_retBox, "bh" + k, new Vector2(sx * (bw - bl * 0.5f), sy * bh), new Vector2(bl, 2f));
            var vt = Child(_retBox, "bv" + k, new Vector2(sx * bw, sy * (bh - bl * 0.5f)), new Vector2(2f, bl));
            hz.gameObject.AddComponent<Image>().color = Cyan; _retCorners[k] = vt.gameObject.AddComponent<Image>(); _retCorners[k].color = Cyan;
        }

        // 라벨 + 리더 라인 (박스 우상단 바깥)
        var lead = Child(_ret, "Leader", new Vector2(-bw, bh + 26f), new Vector2(1f, 52f));
        lead.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f);
        _retTag = Txt(_ret, "TGT-01  KF-21A", Vector2.zero, Vector2.zero, new Vector2(300f, 18f), 12, Ink, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0f, 0.5f), new Vector2(-bw + 10f, bh + 62f));
        _retRange = Txt(_ret, "RNG 0412 M", Vector2.zero, Vector2.zero, new Vector2(300f, 16f), 10, new Color(Ink.r, Ink.g, Ink.b, 0.6f), TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0f, 0.5f), new Vector2(-bw + 10f, bh + 44f));
        _retLock = Txt(_ret, "LOCK: STANDBY", Vector2.zero, Vector2.zero, new Vector2(300f, 16f), 10, Amber, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0f, 0.5f), new Vector2(-bw + 10f, bh + 28f));

        // 락온 확정 플래시(링)
        var fl = Child(_ret, "LockFlash", Vector2.zero, new Vector2(330f, 330f));
        _lockFlash = fl.gameObject.AddComponent<Image>(); UIShapes.Round(_lockFlash, 165); _lockFlash.color = Color.clear; _lockFlash.raycastTarget = false;
        UIShapes.AddBorder(fl, 165, new Color(0.5f, 1f, 0.8f, 0.9f)).color = new Color(0.5f, 1f, 0.8f, 0f);
    }

    // ── 버튼 스피너 ────────────────────────────────────────────────────────
    void BuildSpinner(RectTransform button)
    {
        var sp = new GameObject("Spinner", typeof(RectTransform));
        sp.transform.SetParent(button, false);
        _spinner = (RectTransform)sp.transform;
        _spinner.anchorMin = _spinner.anchorMax = new Vector2(0.5f, 0.5f);
        _spinner.sizeDelta = new Vector2(20f, 20f);
        _spinner.anchoredPosition = new Vector2(-58f, 0f);
        var img = sp.AddComponent<Image>();
        img.sprite = SpinSprite(); img.color = Color.white; img.raycastTarget = false;
        sp.SetActive(false);
    }

    // ── 업링크 터미널 (폼 입력 영역을 덮는다) ────────────────────────────────
    void BuildTerminal(RectTransform form)
    {
        float W = 440f, H = 226f;
        var panel = new GameObject("UplinkTerminal", typeof(RectTransform));
        panel.transform.SetParent(form, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(W, H);
        _term = panel.AddComponent<CanvasGroup>(); _term.alpha = 0f; _term.blocksRaycasts = false; _term.interactable = false;

        var bg = rt.gameObject.AddComponent<Image>(); UIShapes.Round(bg, 8); bg.color = new Color(0.015f, 0.04f, 0.07f, 1f); bg.raycastTarget = false;
        UIShapes.AddBorder(rt, 8, new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f));

        // 헤더 바
        Txt(rt, "UPLINK TERMINAL", Anchor(0f, 1f), new Vector2(16f, -17f), new Vector2(240f, 18f), 11, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0f, 0.5f));
        for (int i = 0; i < 3; i++)
        {
            var d = Child(rt, "hd" + i, Vector2.zero, new Vector2(7f, 7f), Anchor(1f, 1f), new Vector2(-18f - i * 14f, -17f));
            var di = d.gameObject.AddComponent<Image>(); UIShapes.Round(di, 3); di.color = i == 0 ? Amber : new Color(1f, 1f, 1f, 0.25f);
        }
        var sep = Child(rt, "sep", Vector2.zero, new Vector2(W - 32f, 1f), Anchor(0.5f, 1f), new Vector2(0f, -34f));
        sep.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f);

        // 로그 라인 5줄
        string[] names = { "OPEN UPLINK", "HANDSHAKE", "PILOT ID", "TACTICAL NET", "HANGAR ACCESS" };
        for (int i = 0; i < names.Length; i++)
        {
            var ln = new Line();
            float y = -58f - i * 25f;
            ln.left = Txt(rt, "", Anchor(0f, 1f), new Vector2(18f, y), new Vector2(W - 150f, 20f), 13, Ink, TextAnchor.MiddleLeft, FontStyle.Normal, new Vector2(0f, 0.5f));
            ln.right = Txt(rt, "", Anchor(1f, 1f), new Vector2(-18f, y), new Vector2(130f, 20f), 12, Cyan, TextAnchor.MiddleRight, FontStyle.Bold, new Vector2(1f, 0.5f));
            _lines.Add(ln);
        }
        _caret = Txt(rt, "_", Anchor(0f, 1f), new Vector2(18f, -58f), new Vector2(14f, 20f), 13, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(0f, 0.5f));

        // 진행 바 (세그먼트)
        float sw = (W - 36f - (_barSegs.Length - 1) * 3f) / _barSegs.Length;
        for (int i = 0; i < _barSegs.Length; i++)
        {
            var s = Child(rt, "seg" + i, Vector2.zero, new Vector2(sw, 7f), Anchor(0f, 0f), new Vector2(18f + i * (sw + 3f), 16f));
            s.pivot = new Vector2(0f, 0.5f);
            _barSegs[i] = s.gameObject.AddComponent<Image>(); _barSegs[i].color = new Color(1f, 1f, 1f, 0.1f);
        }
        _grantedText = Txt(rt, "", Anchor(0.5f, 0f), new Vector2(0f, 42f), new Vector2(W - 20f, 24f), 17, new Color(_t.success.r, _t.success.g, _t.success.b, 0f), TextAnchor.MiddleCenter, FontStyle.Bold, new Vector2(0.5f, 0.5f));
        UIType.Glow(_grantedText, _t.success, 2f, 0.25f);
    }

    // ── 제어 ──────────────────────────────────────────────────────────────
    public void Play(string pilot = null, string server = null)
    {
        Playing = true; Granted = false; Elapsed = 0f; _grantedT = -1f;
        _pilot = string.IsNullOrEmpty(pilot) ? "PILOT" : pilot.ToUpperInvariant();
        _server = server ?? "";
        if (_spinner != null) _spinner.gameObject.SetActive(true);

        string[] names = { "> OPEN UPLINK", "> HANDSHAKE  " + _server, "> PILOT ID  [" + _pilot + "]", "> TACTICAL NET", "> HANGAR ACCESS" };
        string[] stat = { "OK", "OK", "VERIFIED", "SYNC", "GRANTED" };
        float[] start = { 0.05f, 0.55f, 1.05f, -1f, -1f };       // -1: 승인 후
        for (int i = 0; i < _lines.Count; i++)
        {
            var l = _lines[i]; l.full = names[i]; l.status = stat[i]; l.start = start[i]; l.shown = false;
            l.statusColor = i == 4 ? _t.success : Cyan; l.statusAt = -1f;
            l.left.text = ""; l.right.text = "";
        }
        _grantedText.text = "";
    }

    /// <summary>서버 인증 성공 — 남은 로그 라인이 이어서 출력되고 ACCESS GRANTED 로 마무리</summary>
    public void Complete() { if (Playing) Granted = true; }

    public void Stop()
    {
        Playing = false; Granted = false; _grantedT = -1f;
        if (_spinner != null) _spinner.gameObject.SetActive(false);
    }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        _fadeIn += dt;
        if (_hudGroup != null) _hudGroup.alpha = Mathf.Clamp01((_fadeIn - 0.6f) / 0.9f);

        AnimateHud(dt);

        float termTarget = Playing ? 1f : 0f;
        if (_term != null) _term.alpha = Mathf.MoveTowards(_term.alpha, termTarget, dt * 5f);

        if (!Playing) { RetidleLock(dt); return; }

        Elapsed += dt;
        if (_spinner != null) _spinner.localRotation = Quaternion.Euler(0f, 0f, -Elapsed * 330f);

        // 승인 연출은 최소 시간(1.7s) 이후에 시작: 로그가 너무 빨리 끝나지 않게
        if (Granted && _grantedT < 0f && Elapsed >= 1.7f) _grantedT = 0f;
        if (_grantedT >= 0f) _grantedT += dt;

        UpdateLines(dt);
        UpdateBar();
        UpdateLock();
    }

    void UpdateLines(float dt)
    {
        const float CPS = 55f;
        float verifyT = _grantedT;
        for (int i = 0; i < _lines.Count; i++)
        {
            var l = _lines[i];
            float st = l.start;
            if (st < 0f) { if (_grantedT < 0f) continue; st = Elapsed - _grantedT + (i == 3 ? 0.15f : 0.45f); }
            float n = (Elapsed - st) * CPS;
            if (n < 0f) continue;
            int cnt = Mathf.Min(l.full.Length, (int)n);
            l.left.text = l.full.Substring(0, cnt);
            if (cnt >= l.full.Length)
            {
                if (l.statusAt < 0f) l.statusAt = Elapsed + 0.2f;
                if (Elapsed >= l.statusAt)
                {
                    // PILOT ID 라인은 승인 전까지 VERIFYING 표시
                    bool pending = i == 2 && _grantedT < 0f;
                    l.right.text = pending ? "VERIFYING" + new string('.', 1 + (int)(Elapsed * 4f) % 3) : l.status;
                    l.right.color = pending ? Amber : l.statusColor;
                }
            }
        }
        // 캐럿: 마지막으로 타이핑 중인 줄 끝
        int last = -1; for (int i = 0; i < _lines.Count; i++) if (_lines[i].left.text.Length > 0) last = i;
        if (_caret != null)
        {
            _caret.gameObject.SetActive(last >= 0 && (int)(Elapsed * 3f) % 2 == 0 && _grantedT < 0.6f);
            if (last >= 0)
            {
                var l = _lines[last]; var crt = _caret.rectTransform;
                crt.anchoredPosition = new Vector2(18f + l.left.preferredWidth + 3f, -58f - last * 25f);
            }
        }
        if (_grantedT >= 0.9f)
        {
            _grantedText.text = UIType.Track("ACCESS GRANTED", 1);
            var c = _grantedText.color; c.a = Mathf.Clamp01((_grantedT - 0.9f) / 0.2f); _grantedText.color = c;
        }
    }

    void UpdateBar()
    {
        float target = Granted && _grantedT >= 0f ? Mathf.Clamp01(0.82f + _grantedT * 0.5f) : Mathf.Clamp01(Elapsed / 2.2f) * 0.8f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Elapsed * 12f);
        for (int i = 0; i < _barSegs.Length; i++)
        {
            float p = (i + 1f) / _barSegs.Length;
            bool on = p <= target;
            bool head = on && p > target - 1.5f / _barSegs.Length;
            _barSegs[i].color = on ? (head ? Color.Lerp(Cyan, Color.white, pulse * 0.6f) : new Color(Cyan.r, Cyan.g, Cyan.b, 0.85f)) : new Color(1f, 1f, 1f, 0.1f);
        }
    }

    // ── 레티클 락온 ────────────────────────────────────────────────────────
    float _lock;   // 0 대기 ~ 1 확정
    void UpdateLock()
    {
        float goal = _grantedT >= 0f ? 1f : Mathf.Clamp01(Elapsed / 1.8f) * 0.9f;
        _lock = Mathf.MoveTowards(_lock, goal, Time.unscaledDeltaTime * 1.2f);
        ApplyLock();
        bool locked = _grantedT >= 0.3f;
        _retLock.text = locked ? "LOCK: CONFIRMED" : "LOCK: ACQUIRING" + new string('.', 1 + (int)(Elapsed * 4f) % 3);
        _retLock.color = locked ? _t.success : Amber;
        if (locked && _lockFlash != null)
        {
            float f = Mathf.Clamp01((_grantedT - 0.3f) / 0.5f);
            var rt = _lockFlash.rectTransform; rt.localScale = Vector3.one * (1f + f * 0.5f);
            var b = rt.GetChild(0).GetComponent<Image>(); var c = b.color; c.a = (1f - f) * 0.9f; b.color = c;
        }
    }

    void RetidleLock(float dt)
    {
        _lock = Mathf.MoveTowards(_lock, 0f, dt * 0.8f);
        ApplyLock();
        if (_retLock != null) { _retLock.text = "LOCK: STANDBY"; _retLock.color = Amber; }
    }

    void ApplyLock()
    {
        float e = _lock * _lock * (3f - 2f * _lock);
        _retBox.localScale = Vector3.one * Mathf.Lerp(1.35f, 0.82f, e);
        _retRing.localScale = Vector3.one * Mathf.Lerp(1.12f, 0.94f, e);
        Color c = Color.Lerp(Cyan, _grantedT >= 0.3f ? new Color(0.5f, 1f, 0.8f) : Amber, e);
        for (int k = 0; k < 4; k++) _retCorners[k].color = c;
    }

    // ── 상시 애니메이션 ─────────────────────────────────────────────────────
    void AnimateHud(float dt)
    {
        float t = Time.unscaledTime;
        if (_retDash != null) _retDash.localRotation = Quaternion.Euler(0f, 0f, t * (10f + _lock * 90f));
        if (_ret != null) _ret.anchoredPosition = new Vector2(Mathf.Sin(t * 0.35f) * 5f, Mathf.Sin(t * 0.5f) * 3f);   // 미세 부유
        if (_utc != null) _utc.text = "UTC " + System.DateTime.UtcNow.ToString("HH:mm:ss");
        if (_node != null) _node.text = string.IsNullOrEmpty(_server) ? "NODE  STANDBY" : "NODE  " + _server;
        if (_retRange != null) _retRange.text = "RNG " + (412 + (int)(Mathf.Sin(t * 1.3f) * 6f)).ToString("D4") + " M";
    }

    void AnimateGlitch(float dt)
    {
        if (_glitchT >= 0f)
        {
            _glitchT += dt;
            if (_glitchT > 0.12f) { _glitchT = -1f; foreach (var b in _glitchBars) b.gameObject.SetActive(false); }
            return;
        }
        _nextGlitch -= dt;
        if (_nextGlitch > 0f) return;
        _nextGlitch = Random.Range(3.5f, 8f);
        _glitchT = 0f;
        foreach (var b in _glitchBars)
        {
            b.gameObject.SetActive(Random.value < 0.8f);
            b.anchoredPosition = new Vector2(0f, Random.Range(-480f, 480f));
            b.sizeDelta = new Vector2(Random.Range(-1200f, -200f), Random.Range(1f, 5f));
        }
    }

    // ── 로비 진입 글리치 와이프 ─────────────────────────────────────────────
    // ── 헬퍼 ──────────────────────────────────────────────────────────────
    static Vector2 Anchor(float x, float y) { return new Vector2(x, y); }

    RawImage FullRaw(string name, Texture tex, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(_overlay, false);
        var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var r = go.AddComponent<RawImage>(); r.texture = tex; r.color = c; r.raycastTarget = false;
        return r;
    }

    RectTransform Child(RectTransform parent, string name, Vector2 pos, Vector2 size)
    {
        return Child(parent, name, pos, size, new Vector2(0.5f, 0.5f), pos);
    }

    RectTransform Child(RectTransform parent, string name, Vector2 unusedPos, Vector2 size, Vector2 anchor, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    // anchor 기준 코너에 L자 브래킷(ix,iy: 안쪽 방향)
    void Bracket(float mx, float my, float len, int sx, int sy)
    {
        // sx: +1 좌 / -1 우, sy: +1 상 / -1 하
        float ax = sx > 0 ? 0f : 1f, ay = sy > 0 ? 1f : 0f;
        float x = sx * mx, y = -sy * my;
        var h = Child(_overlay, "bh", Vector2.zero, new Vector2(len, 2f), new Vector2(ax, ay), new Vector2(x + sx * len * 0.5f, y));
        var v = Child(_overlay, "bv", Vector2.zero, new Vector2(2f, len), new Vector2(ax, ay), new Vector2(x, y - sy * len * 0.5f));
        h.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f);
        v.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f);
    }

    Text Txt(RectTransform parent, string s, Vector2 anchor, Vector2 pos, Vector2 size, int fs, Color c, TextAnchor a, FontStyle st, Vector2 pivot, Vector2? posOverride = null)
    {
        var go = new GameObject("T", typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        bool centered = anchor == Vector2.zero && posOverride.HasValue;
        rt.anchorMin = rt.anchorMax = centered ? new Vector2(0.5f, 0.5f) : anchor; rt.pivot = pivot;
        rt.anchoredPosition = posOverride ?? pos; rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.font = _font; t.text = s; t.fontSize = fs; t.color = c; t.alignment = a; t.fontStyle = st; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    // ── 절차적 텍스처/스프라이트 ────────────────────────────────────────────
    static Texture2D _dotTex, _scanTex;
    static Texture2D DotTex()
    {
        if (_dotTex != null) return _dotTex;
        _dotTex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
        var px = new Color32[64]; px[4 * 8 + 4] = new Color32(255, 255, 255, 255);
        _dotTex.SetPixels32(px); _dotTex.Apply(false);
        return _dotTex;
    }
    static Texture2D ScanTex()
    {
        if (_scanTex != null) return _scanTex;
        _scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
        _scanTex.SetPixels32(new[] { new Color32(255, 255, 255, 90), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0) });
        _scanTex.Apply(false);
        return _scanTex;
    }

    // 점선 링
    static Sprite DashSprite()
    {
        if (_dashSprite != null) return _dashSprite;
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var px = new Color32[S * S];
        float c = (S - 1) * 0.5f, r1 = S * 0.5f - 2f, r0 = r1 - 3.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(Mathf.Min(d - r0, r1 - d) / 1.2f);
                float ang = (Mathf.Atan2(dy, dx) + Mathf.PI) / (Mathf.PI * 2f);
                float dash = Mathf.Repeat(ang * 36f, 1f) < 0.55f ? 1f : 0f;
                // 4곳은 길게 비워 방향 느낌
                float gap = Mathf.Repeat(ang * 4f, 1f) < 0.08f ? 0f : 1f;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(ring * dash * gap * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false);
        _dashSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return _dashSprite;
    }

    // 회전 스피너: 꼬리가 옅어지는 원호
    static Sprite SpinSprite()
    {
        if (_spinSprite != null) return _spinSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var px = new Color32[S * S];
        float c = (S - 1) * 0.5f, r1 = S * 0.5f - 1.5f, r0 = r1 - 6f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(Mathf.Min(d - r0, r1 - d) / 1.2f);
                float ang = (Mathf.Atan2(dy, dx) + Mathf.PI) / (Mathf.PI * 2f);
                float a = ring * Mathf.Pow(ang, 1.6f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false);
        _spinSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return _spinSprite;
    }
}
