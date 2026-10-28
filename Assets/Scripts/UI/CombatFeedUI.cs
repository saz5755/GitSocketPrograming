using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 결과 UI — 킬 피드(우상단), 히트 마커(화면 중앙), 단발 알림, 사망 화면, 스코어보드(` 홀드).
/// 문구는 실제 공중전 교신 용어(brevity)를 따른다: 격추 확인 = "SPLASH", 사출 = "EJECTED".
/// 콕핏 HUD 와 같은 스프라이트 키트(Resources/HUD)와 팔레트를 쓴다.
/// </summary>
public class CombatFeedUI : MonoBehaviour
{
    public static CombatFeedUI Instance { get; private set; }

    static readonly Color HG    = new Color(0.20f, 1.00f, 0.55f, 0.95f);
    static readonly Color HGD   = new Color(0.20f, 1.00f, 0.55f, 0.66f);
    static readonly Color INK   = new Color(0.90f, 1.00f, 0.95f, 0.96f);
    static readonly Color DIM   = new Color(0.70f, 0.80f, 0.76f, 0.80f);
    static readonly Color WARN  = new Color(0.96f, 0.72f, 0.29f, 1.00f);
    static readonly Color CRIT  = new Color(0.94f, 0.36f, 0.41f, 1.00f);
    static readonly Color CYAN  = new Color(0.28f, 0.84f, 0.95f, 1.00f);
    static readonly Color GLASS = new Color(0.02f, 0.05f, 0.08f, 0.72f);
    static readonly Color EDGE  = new Color(0.20f, 1.00f, 0.55f, 0.30f);
    static readonly Color HALO  = new Color(0.00f, 0.05f, 0.03f, 0.75f);

    const int   FeedMax      = 5;
    const float FeedLife     = 7f;
    const float FeedRowH     = 34f;

    Font          _font;
    RectTransform _root, _feedRoot;

    // 킬 피드
    class FeedRow { public RectTransform rt; public CanvasGroup cg; public float born; }
    readonly List<FeedRow> _feed = new();

    // 히트 마커
    RectTransform _hitRoot;
    Image[]       _hitTicks;
    float         _hitTimer;
    float         _hitScale = 1f;

    // 중앙 알림 / SPLASH 콜아웃
    Text  _notice;   float _noticeTimer;
    Text  _callout;  Text _calloutSub;  float _calloutTimer;

    // 사망 화면
    RectTransform _death, _deathCard;
    CanvasGroup   _deathCg;
    Text _deathTitle, _deathBy, _deathStats, _deathPrompt;

    // 스코어보드
    RectTransform _board;
    RectTransform _boardRows;
    readonly List<Text[]> _rowTexts = new();
    readonly List<Image>  _rowBacks = new();
    ScoreboardPacket _scores;

    static string MyNick => NetworkManager.Instance?.socketClient?.myNickname ?? "";

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
    }

    void OnEnable()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnPlayerKilled += HandleKill;
        sc.OnScoreboard   += HandleScoreboard;
        if (sc.LastScoreboard != null) HandleScoreboard(sc.LastScoreboard);
    }

    void OnDisable()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnPlayerKilled -= HandleKill;
        sc.OnScoreboard   -= HandleScoreboard;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── 공개 API ─────────────────────────────────────────────────────────────
    public void ShowHitMarker(bool heavy)
    {
        _hitTimer = heavy ? 0.45f : 0.18f;
        _hitScale = heavy ? 1.6f : 1f;
        var c = heavy ? WARN : INK;
        foreach (var t in _hitTicks) t.color = c;
    }

    public void ShowNotice(string msg, bool warn = false)
    {
        _notice.text  = msg;
        _notice.color = warn ? WARN : INK;
        _noticeTimer  = 1.4f;
    }

    public void ShowDeathScreen()
    {
        var ps = PilotStatus.Instance;
        if (ps == null) return;
        (string title, Color col) = ps.DeathCause switch
        {
            PilotStatus.CauseShotDown    => ("SHOT DOWN",    CRIT),
            PilotStatus.CauseWaterImpact => ("WATER IMPACT", CRIT),
            PilotStatus.CauseCrash       => ("AIRCRAFT LOST", CRIT),
            PilotStatus.CauseEjected     => ("EJECTED",      WARN),
            _                            => ("AIRCRAFT LOST", CRIT),
        };
        _deathTitle.text  = title;
        _deathTitle.color = col;
        _deathBy.text = string.IsNullOrEmpty(ps.Killer)
            ? CauseDetail(ps.DeathCause)
            : $"KILLED BY  {ps.Killer}   ·   {WeaponName(ps.KillerWeapon)}";
        int t = Mathf.FloorToInt(ps.SortieTime);
        _deathStats.text = $"SORTIE  {t / 60:00}:{t % 60:00}        SPLASH  {ps.SortieKills}";
        _death.gameObject.SetActive(true);
        _deathCg.alpha = 0f;
    }

    public void HideDeathScreen() => _death.gameObject.SetActive(false);

    // ── 네트워크 이벤트 ─────────────────────────────────────────────────────
    void HandleKill(PlayerKilledPacket p)
    {
        string me = MyNick;
        AddFeedRow(p, me);

        if (p.killer == me && p.victim != me)
        {
            int n = PilotStatus.Instance != null ? PilotStatus.Instance.SortieKills : 1;
            _callout.text    = $"SPLASH {CountWord(n)}";
            _calloutSub.text = p.victim;
            _calloutTimer    = 2.6f;
        }
    }

    void HandleScoreboard(ScoreboardPacket p)
    {
        _scores = p;
        if (_board.gameObject.activeSelf) RefreshBoard();
    }

    // ── 매 프레임 ─────────────────────────────────────────────────────────────
    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // 킬 피드 페이드
        for (int i = _feed.Count - 1; i >= 0; i--)
        {
            var r = _feed[i];
            float age = Time.unscaledTime - r.born;
            if (age > FeedLife) { Destroy(r.rt.gameObject); _feed.RemoveAt(i); continue; }
            r.cg.alpha = Mathf.Clamp01((FeedLife - age) / 1.2f) * Mathf.Clamp01(age / 0.15f);
        }
        for (int i = 0; i < _feed.Count; i++)
        {
            var target = new Vector2(0f, -i * (FeedRowH + 6f));
            _feed[i].rt.anchoredPosition = Vector2.Lerp(_feed[i].rt.anchoredPosition, target, 1f - Mathf.Exp(-14f * dt));
        }

        // 히트 마커
        if (_hitTimer > 0f)
        {
            _hitTimer -= dt;
            _hitRoot.gameObject.SetActive(true);
            float a = Mathf.Clamp01(_hitTimer / 0.12f);
            foreach (var t in _hitTicks) { var c = t.color; c.a = a; t.color = c; }
            _hitRoot.localScale = Vector3.one * Mathf.Lerp(_hitScale, _hitScale * 1.25f, 1f - a);
        }
        else if (_hitRoot.gameObject.activeSelf) _hitRoot.gameObject.SetActive(false);

        // 알림 / 콜아웃
        _noticeTimer -= dt;
        _notice.gameObject.SetActive(_noticeTimer > 0f);
        _calloutTimer -= dt;
        bool co = _calloutTimer > 0f;
        _callout.gameObject.SetActive(co);
        _calloutSub.gameObject.SetActive(co);
        if (co)
        {
            float a = Mathf.Clamp01(_calloutTimer / 0.5f);
            _callout.color    = new Color(HG.r, HG.g, HG.b, a);
            _calloutSub.color = new Color(INK.r, INK.g, INK.b, a * 0.85f);
        }

        // 사망 화면
        if (_death.gameObject.activeSelf)
        {
            _deathCg.alpha = Mathf.MoveTowards(_deathCg.alpha, 1f, dt * 1.5f);
            var ps = PilotStatus.Instance;
            if (ps != null)
            {
                if (ps.RespawnReadyAt < 0f) _deathPrompt.text = "";
                else
                {
                    float left = ps.RespawnReadyAt - Time.time;
                    _deathPrompt.text = left > 0f
                        ? $"RETURN TO CARRIER IN  {Mathf.CeilToInt(left)}"
                        : $"[{SimInput.ConfirmKey}]   RETURN TO CARRIER";
                    _deathPrompt.color = left > 0f ? DIM : HG;
                }
            }
        }

        // 스코어보드 (홀드)
        bool showBoard = SimInput.ScoreboardHeld && RoomModeSO.ScoreboardShown;
        if (showBoard != _board.gameObject.activeSelf)
        {
            _board.gameObject.SetActive(showBoard);
            if (showBoard) RefreshBoard();
        }
        _deathCard.gameObject.SetActive(!showBoard);
    }

    // ── 킬 피드 행 ─────────────────────────────────────────────────────────────
    void AddFeedRow(PlayerKilledPacket p, string me)
    {
        if (!RoomModeSO.CombatFeedShown) return;   // 에어쇼: 킬 피드 없음
        string victim = Colored(p.victim, me);
        string line;
        if (!string.IsNullOrEmpty(p.killer) && p.killer != p.victim)
            line = $"{Colored(p.killer, me)}   <color=#8FA39A>{WeaponTag(p.weapon)}</color>   {victim}";
        else
            line = $"{victim}   <color=#8FA39A>{CauseTag(p.cause)}</color>";

        bool involvesMe = p.victim == me || p.killer == me;
        var row = new GameObject("FeedRow", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        row.SetParent(_feedRoot, false);
        row.anchorMin = row.anchorMax = row.pivot = new Vector2(1f, 1f);
        row.anchoredPosition = new Vector2(0f, 20f);

        var txt = AddText(row, line, 19, INK, TextAnchor.MiddleRight);
        txt.supportRichText = true;
        float w = Mathf.Clamp(txt.preferredWidth + 36f, 180f, 640f);
        row.sizeDelta = new Vector2(w, FeedRowH);
        var tr = txt.rectTransform; Stretch(tr); tr.offsetMin = new Vector2(16f, 0f); tr.offsetMax = new Vector2(-16f, 0f);

        var back = Img(row, HudSprites.Pill, involvesMe ? new Color(0.06f, 0.16f, 0.11f, 0.82f) : GLASS);
        Stretch(back.rectTransform); back.transform.SetAsFirstSibling();
        var line2 = Img(row, HudSprites.PillLine, involvesMe ? HGD : EDGE);
        Stretch(line2.rectTransform); line2.transform.SetSiblingIndex(1);

        _feed.Insert(0, new FeedRow { rt = row, cg = row.GetComponent<CanvasGroup>(), born = Time.unscaledTime });
        while (_feed.Count > FeedMax)
        {
            var old = _feed[_feed.Count - 1];
            Destroy(old.rt.gameObject);
            _feed.RemoveAt(_feed.Count - 1);
        }
    }

    static string Colored(string nick, string me)
    {
        string hex = nick == me ? "#5CFF9D"
                   : AIManager.IsEnemyBot(nick) ? "#F05B68"
                   : AIManager.IsEscortBot(nick) ? "#48D6F2"
                   : "#E6FFF2";
        return $"<color={hex}>{nick}</color>";
    }

    static string WeaponTag(string w) => w == CombatDamage.WeaponMissile ? "▸ MSL ▸" : w == CombatDamage.WeaponGun ? "▸ GUN ▸" : "▸";
    static string WeaponName(string w) => w == CombatDamage.WeaponMissile ? "MISSILE" : w == CombatDamage.WeaponGun ? "M61 GUN" : "—";
    static string CauseTag(string c) => c switch
    {
        PilotStatus.CauseWaterImpact => "WATER IMPACT",
        PilotStatus.CauseCrash       => "CRASHED",
        PilotStatus.CauseEjected     => "EJECTED",
        _                            => "LOST",
    };
    static string CauseDetail(string c) => c switch
    {
        PilotStatus.CauseWaterImpact => "CONTROLLED FLIGHT INTO WATER",
        PilotStatus.CauseCrash       => "COLLISION / HARD LANDING",
        PilotStatus.CauseEjected     => "PILOT EJECTED  ·  AIRFRAME LOST",
        _                            => "",
    };
    static string CountWord(int n) => n switch { 1 => "ONE", 2 => "TWO", 3 => "THREE", 4 => "FOUR", 5 => "FIVE", _ => n.ToString() };

    // ── 스코어보드 ────────────────────────────────────────────────────────────
    void RefreshBoard()
    {
        var list = _scores?.entries ?? new List<ScoreEntry>();
        list.Sort((a, b) =>
        {
            int c = (b.kills + b.aiKills).CompareTo(a.kills + a.aiKills);
            return c != 0 ? c : a.deaths.CompareTo(b.deaths);
        });

        while (_rowTexts.Count < list.Count) AddBoardRow();
        string me = MyNick;
        for (int i = 0; i < _rowTexts.Count; i++)
        {
            bool on = i < list.Count;
            _rowBacks[i].gameObject.SetActive(on);
            if (!on) continue;
            var e = list[i];
            var t = _rowTexts[i];
            bool mine = e.nickname == me;
            t[0].text = $"{i + 1}";
            t[1].text = e.nickname;
            t[2].text = $"{e.kills}";
            t[3].text = $"{e.aiKills}";
            t[4].text = $"{e.deaths}";
            foreach (var x in t) x.color = mine ? HG : INK;
            _rowBacks[i].color = mine ? new Color(0.06f, 0.18f, 0.12f, 0.75f) : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.015f : 0f);   // 리니어 색공간이라 알파가 밝게 보임
        }
        _board.sizeDelta = new Vector2(_board.sizeDelta.x, 132f + Mathf.Max(1, list.Count) * 38f);
    }

    void AddBoardRow()
    {
        int i = _rowTexts.Count;
        var back = Img(_boardRows, null, Color.clear);
        var rt = back.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 36f);
        rt.anchoredPosition = new Vector2(0f, -i * 38f);
        var cells = new Text[5];
        for (int c = 0; c < 5; c++)
        {
            var t = AddText(rt, "", 20, INK, BoardCols[c].align);
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 0.5f);
            trt.anchoredPosition = new Vector2(BoardCols[c].x, 0f);
            trt.sizeDelta = new Vector2(BoardCols[c].w, 0f);
            cells[c] = t;
        }
        _rowTexts.Add(cells);
        _rowBacks.Add(back);
    }

    static readonly (float x, float w, TextAnchor align, string title)[] BoardCols =
    {
        (24f,  40f,  TextAnchor.MiddleLeft,   "#"),
        (70f,  300f, TextAnchor.MiddleLeft,   "PILOT"),
        (380f, 80f,  TextAnchor.MiddleCenter, "KILLS"),
        (470f, 80f,  TextAnchor.MiddleCenter, "AI"),
        (560f, 80f,  TextAnchor.MiddleCenter, "DEATHS"),
    };

    // ── 빌드 ──────────────────────────────────────────────────────────────────
    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        _root = (RectTransform)transform;

        // 킬 피드 — 우상단 상황 패널 바로 아래 (추격 시점 고도 테이프 위쪽 여백)
        _feedRoot = NewRect("KillFeed", _root);
        _feedRoot.anchorMin = _feedRoot.anchorMax = _feedRoot.pivot = new Vector2(1f, 1f);
        _feedRoot.anchoredPosition = new Vector2(-36f, -104f);
        _feedRoot.sizeDelta = new Vector2(640f, 260f);

        // 히트 마커 — 중앙 X 형 4틱
        _hitRoot = NewRect("HitMarker", _root);
        _hitRoot.sizeDelta = new Vector2(60f, 60f);
        _hitTicks = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            float ang = 45f + 90f * i;
            var tick = Img(_hitRoot, null, INK);
            var r = tick.rectTransform;
            r.sizeDelta = new Vector2(14f, 3f);
            Vector2 dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
            r.anchoredPosition = dir * 17f;
            r.localRotation = Quaternion.Euler(0f, 0f, ang);
            _hitTicks[i] = tick;
        }
        _hitRoot.gameObject.SetActive(false);

        // 단발 알림 (사출 핸들 등)
        _notice = AddText(_root, "", 26, INK, TextAnchor.MiddleCenter, halo: true);
        _notice.rectTransform.anchoredPosition = new Vector2(0f, -170f);
        _notice.rectTransform.sizeDelta = new Vector2(800f, 40f);
        _notice.gameObject.SetActive(false);

        // SPLASH 콜아웃
        _callout = AddText(_root, "", 44, HG, TextAnchor.MiddleCenter, halo: true);
        _callout.fontStyle = FontStyle.Bold;
        _callout.rectTransform.anchoredPosition = new Vector2(0f, 190f);
        _callout.rectTransform.sizeDelta = new Vector2(900f, 60f);
        _calloutSub = AddText(_root, "", 22, INK, TextAnchor.MiddleCenter, halo: true);
        _calloutSub.rectTransform.anchoredPosition = new Vector2(0f, 150f);
        _calloutSub.rectTransform.sizeDelta = new Vector2(900f, 32f);
        _callout.gameObject.SetActive(false);
        _calloutSub.gameObject.SetActive(false);

        BuildDeathScreen();
        BuildScoreboard();
    }

    void BuildDeathScreen()
    {
        _death = NewRect("DeathScreen", _root);
        Stretch(_death);
        _deathCg = _death.gameObject.AddComponent<CanvasGroup>();
        _deathCg.interactable = _deathCg.blocksRaycasts = false;

        var vig = Img(_death, HudSprites.Vignette, new Color(0f, 0f, 0f, 0.85f));
        Stretch(vig.rectTransform);
        var dim = Img(_death, null, new Color(0f, 0.01f, 0.02f, 0.35f));
        Stretch(dim.rectTransform);

        // 하단 1/3 — 화면 중앙의 잔해·사망 카메라를 가리지 않게
        var card = NewRect("Card", _death);
        card.sizeDelta = new Vector2(760f, 250f);
        card.anchoredPosition = new Vector2(0f, -270f);
        _deathCard = card;
        var fill = Img(card, HudSprites.Panel, GLASS);        Stretch(fill.rectTransform);
        var edge = Img(card, HudSprites.PanelLine, EDGE);     Stretch(edge.rectTransform);
        var acc  = Img(card, HudSprites.PanelAccent, CRIT);   Stretch(acc.rectTransform);

        _deathTitle = AddText(card, "", 54, CRIT, TextAnchor.MiddleCenter, halo: true);
        _deathTitle.fontStyle = FontStyle.Bold;
        _deathTitle.rectTransform.anchoredPosition = new Vector2(0f, 64f);
        _deathTitle.rectTransform.sizeDelta = new Vector2(740f, 70f);

        _deathBy = AddText(card, "", 21, INK, TextAnchor.MiddleCenter);
        _deathBy.rectTransform.anchoredPosition = new Vector2(0f, 10f);
        _deathBy.rectTransform.sizeDelta = new Vector2(740f, 30f);

        var div = Img(card, HudSprites.GlowLine, EDGE);
        div.rectTransform.sizeDelta = new Vector2(560f, 2f);
        div.rectTransform.anchoredPosition = new Vector2(0f, -22f);

        _deathStats = AddText(card, "", 19, DIM, TextAnchor.MiddleCenter);
        _deathStats.rectTransform.anchoredPosition = new Vector2(0f, -50f);
        _deathStats.rectTransform.sizeDelta = new Vector2(740f, 28f);

        _deathPrompt = AddText(card, "", 22, DIM, TextAnchor.MiddleCenter);
        _deathPrompt.rectTransform.anchoredPosition = new Vector2(0f, -94f);
        _deathPrompt.rectTransform.sizeDelta = new Vector2(740f, 30f);

        _death.gameObject.SetActive(false);
    }

    void BuildScoreboard()
    {
        _board = NewRect("Scoreboard", _root);
        _board.sizeDelta = new Vector2(680f, 200f);
        _board.anchoredPosition = new Vector2(0f, 60f);
        var fill = Img(_board, HudSprites.Panel, GLASS);    Stretch(fill.rectTransform);
        var edge = Img(_board, HudSprites.PanelLine, EDGE); Stretch(edge.rectTransform);
        var acc  = Img(_board, HudSprites.PanelAccent, HGD); Stretch(acc.rectTransform);

        var title = AddText(_board, "SQUADRON  ·  SORTIE RESULTS", 18, HGD, TextAnchor.MiddleLeft);
        var trt = title.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(24f, -16f);
        trt.sizeDelta = new Vector2(600f, 28f);

        var header = NewRect("Header", _board);
        header.anchorMin = new Vector2(0f, 1f); header.anchorMax = new Vector2(1f, 1f); header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0f, -54f);
        header.sizeDelta = new Vector2(0f, 30f);
        foreach (var col in BoardCols)
        {
            var t = AddText(header, col.title, 15, DIM, col.align);
            var r = t.rectTransform;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(col.x, 0f);
            r.sizeDelta = new Vector2(col.w, 0f);
        }
        var line = Img(_board, HudSprites.GlowLine, EDGE);
        var lrt = line.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
        lrt.sizeDelta = new Vector2(630f, 2f);
        lrt.anchoredPosition = new Vector2(0f, -88f);

        _boardRows = NewRect("Rows", _board);
        _boardRows.anchorMin = new Vector2(0f, 1f); _boardRows.anchorMax = new Vector2(1f, 1f); _boardRows.pivot = new Vector2(0.5f, 1f);
        _boardRows.offsetMin = new Vector2(12f, 0f); _boardRows.offsetMax = new Vector2(-12f, 0f);
        _boardRows.anchoredPosition = new Vector2(0f, -96f);
        _boardRows.sizeDelta = new Vector2(-24f, 400f);

        _board.gameObject.SetActive(false);
    }

    // ── UI 헬퍼 ────────────────────────────────────────────────────────────────
    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Image Img(Transform parent, string sprite, Color color)
    {
        var img = new GameObject(sprite ?? "Rect", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.raycastTarget = false;
        if (sprite != null)
        {
            img.sprite = HudSprites.Get(sprite);
            img.type   = Image.Type.Sliced;
        }
        img.color = color;
        return img;
    }

    Text AddText(Transform parent, string s, int size, Color color, TextAnchor align, bool halo = false)
    {
        var t = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = _font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.text = s;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        if (halo)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = HALO;
            o.effectDistance = new Vector2(1.5f, -1.5f);
        }
        return t;
    }
}
