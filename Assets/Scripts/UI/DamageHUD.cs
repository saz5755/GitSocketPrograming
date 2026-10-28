using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 피격 데미지 HUD (v4.2 C-5) — 기체 손상 실루엣 + 경고등 + 피격 방향 플래시 + 경보음.
/// 손상이 있을 때만 카드가 나타나며(평소 화면을 가리지 않게), 콕핏·추격 시점 모두에 표시된다.
/// 스타일은 콕핏 HUD 스프라이트 키트(Resources/HUD)를 따른다. GameModeManager 가 런타임에 생성한다.
/// </summary>
public class DamageHUD : MonoBehaviour
{
    static readonly Color HG    = new Color(0.20f, 1.00f, 0.55f, 0.95f);
    static readonly Color HGD   = new Color(0.20f, 1.00f, 0.55f, 0.62f);
    static readonly Color Ink   = new Color(0.90f, 1.00f, 0.95f, 0.96f);
    static readonly Color Glass = new Color(0.01f, 0.05f, 0.04f, 0.66f);
    static readonly Color Edge  = new Color(0.20f, 1.00f, 0.55f, 0.30f);
    static readonly Color Warn  = new Color(1.00f, 0.72f, 0.25f, 1f);
    static readonly Color Crit  = new Color(1.00f, 0.30f, 0.34f, 1f);
    static readonly Color Off   = new Color(0.20f, 1.00f, 0.55f, 0.14f);

    Font _font;
    CanvasGroup _cardCg;
    Image _engine, _wingL, _wingR, _body, _hyd;
    Text  _lampFire, _lampHyd, _lampWingL, _lampWingR, _lampLeak;
    readonly Image[] _edges = new Image[4];   // 상(전방) 하(후방) 좌 우
    readonly float[] _edgeA = new float[4];

    AudioSource _audio;
    AudioClip   _fireAlarm, _chime;
    float       _alarmTimer;
    bool        _hydWasLow;

    PlayerController _pc;

    void Awake()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 0f; _audio.volume = 0.55f; _audio.playOnAwake = false;
        _fireAlarm = Tone(new[] { 520f, 0f, 520f }, 0.12f);
        _chime     = Tone(new[] { 880f, 660f }, 0.16f);
        AircraftDamage.OnHitDirection += OnHit;
    }

    void OnDestroy() => AircraftDamage.OnHitDirection -= OnHit;

    void OnHit(Vector3 local)
    {
        // 기체 로컬 방향 → 화면 가장자리 (앞=위, 뒤=아래)
        float ax = Mathf.Abs(local.x), az = Mathf.Abs(local.z);
        int i = az >= ax ? (local.z >= 0f ? 0 : 1) : (local.x < 0f ? 2 : 3);
        _edgeA[i] = 1f;
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
        bool flying = _pc != null && gm != null && gm.IsFlying && !gm.IsDead;
        var dmg = flying ? _pc.Damage : null;

        float dt = Time.deltaTime;
        for (int i = 0; i < 4; i++)
        {
            _edgeA[i] = flying ? Mathf.MoveTowards(_edgeA[i], 0f, dt / 0.6f) : 0f;
            _edges[i].color = new Color(Crit.r, Crit.g, Crit.b, _edgeA[i] * 0.85f);
        }

        bool show = dmg != null && dmg.AnyDamage;
        _cardCg.alpha = Mathf.MoveTowards(_cardCg.alpha, show ? 1f : 0f, dt * 4f);
        if (!show) { _hydWasLow = false; return; }

        bool blink = Mathf.Repeat(Time.time, 0.6f) < 0.36f;
        _engine.color = PartColor(dmg.Health(AircraftDamage.Part.Engine), blink);
        _wingL.color  = PartColor(dmg.Health(AircraftDamage.Part.WingL),  blink);
        _wingR.color  = PartColor(dmg.Health(AircraftDamage.Part.WingR),  blink);
        _hyd.color    = PartColor(dmg.Health(AircraftDamage.Part.Hydraulics), blink);
        _body.color   = PartColor(_pc.CurrentHP / Mathf.Max(1f, _pc.MaxHP), blink);

        Lamp(_lampFire,  dmg.EngineFire, Crit, blink);
        Lamp(_lampHyd,   dmg.HydLow,     Warn, blink);
        Lamp(_lampWingL, dmg.Failed(AircraftDamage.Part.WingL), Warn, blink);
        Lamp(_lampWingR, dmg.Failed(AircraftDamage.Part.WingR), Warn, blink);
        Lamp(_lampLeak,  dmg.FuelLeak,   Warn, true);

        // 경보음: 엔진 화재는 연속 경보, 유압 저하는 진입 시 1회
        if (dmg.EngineFire)
        {
            _alarmTimer -= dt;
            if (_alarmTimer <= 0f) { _audio.PlayOneShot(_fireAlarm); _alarmTimer = 0.9f; }
        }
        if (dmg.HydLow && !_hydWasLow) _audio.PlayOneShot(_chime);
        _hydWasLow = dmg.HydLow;
    }

    static Color PartColor(float health, bool blink)
    {
        if (health <= 0f)  return blink ? Crit : new Color(Crit.r, Crit.g, Crit.b, 0.35f);
        if (health < 0.5f) return Warn;
        if (health < 1f)   return Color.Lerp(Warn, HG, (health - 0.5f) * 2f);
        return HGD;
    }

    static void Lamp(Text t, bool on, Color c, bool blink)
    {
        t.color = on ? (blink ? c : new Color(c.r, c.g, c.b, 0.55f)) : Off;
    }

    // ── UI ──────────────────────────────────────────────────────────────────
    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;   // 추격 HUD(14) 위
        var cs = gameObject.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;
        var root = transform;

        // 피격 방향 플래시 (화면 가장자리 글로우)
        Vector2[] anchors = { new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f) };
        float[]   rots    = { 180f, 0f, -90f, 90f };
        for (int i = 0; i < 4; i++)
        {
            var rt = R("HitEdge", root, Vector2.zero, i < 2 ? new Vector2(1400f, 160f) : new Vector2(900f, 160f));
            rt.anchorMin = rt.anchorMax = anchors[i];
            rt.pivot = new Vector2(0.5f, 0f);
            rt.localRotation = Quaternion.Euler(0f, 0f, rots[i]);
            _edges[i] = Spr(rt, HudSprites.Glow, Color.clear);
        }

        // 손상 카드 (무장 카드 왼쪽)
        var card = R("DamageCard", root, new Vector2(500f, -390f), new Vector2(200f, 150f));
        _cardCg = card.gameObject.AddComponent<CanvasGroup>();
        _cardCg.alpha = 0f; _cardCg.blocksRaycasts = false;
        Spr(card, HudSprites.Panel, Glass, Image.Type.Sliced);
        Spr(Fill(card), HudSprites.PanelLine, Edge, Image.Type.Sliced);
        Spr(Fill(card), HudSprites.PanelAccent, new Color(HG.r, HG.g, HG.b, 0.75f), Image.Type.Sliced);
        Txt(card, "DAMAGE", new Vector2(-88f, 58f), new Vector2(100f, 18f), 11, HGD, TextAnchor.MiddleLeft, FontStyle.Bold);

        // 기체 실루엣 (위에서 본 단순 도형 — 기수 위)
        var sil = R("Silhouette", card, new Vector2(-45f, -6f), new Vector2(90f, 110f));
        _body   = Box(R("Body",  sil, new Vector2(0f, 6f),    new Vector2(12f, 84f)));
        _wingL  = Box(R("WingL", sil, new Vector2(-22f, -2f), new Vector2(34f, 14f)));
        _wingR  = Box(R("WingR", sil, new Vector2(22f, -2f),  new Vector2(34f, 14f)));
        _engine = Box(R("Eng",   sil, new Vector2(0f, -42f),  new Vector2(18f, 16f)));
        _hyd    = Box(R("Hyd",   sil, new Vector2(0f, 22f),   new Vector2(6f, 10f)));
        Box(R("TailL", sil, new Vector2(-12f, -34f), new Vector2(12f, 6f))).color = HGD;
        Box(R("TailR", sil, new Vector2(12f, -34f),  new Vector2(12f, 6f))).color = HGD;

        // 경고등
        float x = 10f, y = 36f;
        _lampFire  = Txt(card, "ENG FIRE",  new Vector2(x, y),        new Vector2(90f, 18f), 12, Off, TextAnchor.MiddleLeft, FontStyle.Bold);
        _lampHyd   = Txt(card, "HYD LOW",   new Vector2(x, y - 20f),  new Vector2(90f, 18f), 12, Off, TextAnchor.MiddleLeft, FontStyle.Bold);
        _lampWingL = Txt(card, "WING L",    new Vector2(x, y - 40f),  new Vector2(90f, 18f), 12, Off, TextAnchor.MiddleLeft, FontStyle.Bold);
        _lampWingR = Txt(card, "WING R",    new Vector2(x, y - 60f),  new Vector2(90f, 18f), 12, Off, TextAnchor.MiddleLeft, FontStyle.Bold);
        _lampLeak  = Txt(card, "FUEL LEAK", new Vector2(x, y - 80f),  new Vector2(90f, 18f), 12, Off, TextAnchor.MiddleLeft, FontStyle.Bold);
    }

    RectTransform R(string n, Transform p, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(n, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(p, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    RectTransform Fill(RectTransform p)
    {
        var rt = R("Fill", p, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Image Spr(RectTransform rt, string sprite, Color c, Image.Type type = Image.Type.Simple)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = HudSprites.Get(sprite); img.type = type;
        img.color = c; img.raycastTarget = false;
        return img;
    }

    static Image Box(RectTransform rt)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = HGD; img.raycastTarget = false;
        return img;
    }

    Text Txt(Transform p, string s, Vector2 pos, Vector2 size, int fs, Color c, TextAnchor a, FontStyle st)
    {
        var rt = R("T", p, pos, size);
        if (a == TextAnchor.MiddleLeft) rt.pivot = new Vector2(0f, 0.5f);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = _font; t.text = s; t.fontSize = fs; t.color = c; t.alignment = a; t.fontStyle = st;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
        return t;
    }

    static AudioClip Tone(float[] freqs, float seg)
    {
        const int SR = 22050;
        int per = (int)(seg * SR), len = per * freqs.Length;
        var d = new float[len];
        for (int k = 0; k < freqs.Length; k++)
        {
            if (freqs[k] <= 0f) continue;
            for (int s = 0; s < per; s++)
            {
                float env = Mathf.Min(1f, Mathf.Min(s, per - s) / (SR * 0.006f));
                d[k * per + s] = env * 0.4f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freqs[k] * s / SR)) * 0.6f;
            }
        }
        var clip = AudioClip.Create("dmg_tone", len, 1, SR, false);
        clip.SetData(d, 0);
        return clip;
    }
}
