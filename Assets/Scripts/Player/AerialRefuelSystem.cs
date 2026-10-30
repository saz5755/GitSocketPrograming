using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공중급유 (프로브-드로그) — 로컬 조종사 전용 로직 + 안내 HUD.
///
/// 절차 (실제 해군 AAR 과 같은 흐름):
///   1. 랑데부   — 급유기 궤도로 접근 (HUD: 거리·방위·고도차, 연료 부족 시 방향 안내)
///   2. 프리컨택 — 1.2 km 안에서 프로브 전개, 바스켓 뒤 수 m 에 안정
///   3. 컨택     — 프로브 끝을 바스켓에 천천히(접근속도 ≤ MaxContactClosure) 꽂는다. 너무 빠르면 튕겨 나온다
///   4. 급유     — 허용 범위(좌우·상하 ±Envelope, 앞뒤 밀어넣기 범위) 안에서 자세를 유지하면 연료가 들어온다
///   5. 분리     — 만탱크 후 감속해 빠지거나, 범위를 벗어나면 자동 분리
/// 연료는 각자 로컬 상태라 별도 패킷 없이, 접속 여부만 MOVE fx 비트로 알려 다른 화면에서도 바스켓이 물려 보인다.
/// AIManager 가 같은 오브젝트에 붙인다.
/// </summary>
public class AerialRefuelSystem : MonoBehaviour
{
    const float ProbeOutRange      = 1200f;
    const float HudRange           = 4000f;
    const float CaptureRadius      = 1.1f;    // 프로브 끝 ↔ 바스켓 중심 (게임 친화 — 실제 바스켓 지름 약 0.7m)
    const float MaxContactClosure  = 3.0f;    // m/s 이상이면 바스켓이 튕겨 나간다
    const float LateralEnvelope    = 2.4f;    // 접속 유지 — 바스켓 기준 좌우·상하 (m)
    const float PushInMax          = 5.0f;    // 앞으로 밀어 넣을 수 있는 호스 여유
    const float PullBackMax        = 1.8f;    // 뒤로 빠지면 분리
    const float FlowRate           = 420f;    // 연료/초 (만탱크 10000 → 약 24초)

    enum State { Off, Rendezvous, Precontact, Contact }
    State _state;

    // ── 조회 (FlightCoach 튜토리얼이 읽는다) ─────────────────────────────
    public static AerialRefuelSystem Instance { get; private set; }
    public bool  HasTanker      { get; private set; }
    public float TankerDistance { get; private set; } = float.MaxValue;
    public float TankerBearing  { get; private set; }
    public float TankerAltDiff  { get; private set; }
    public float DrogueDistance => _prevDist;
    public float Closure        => _closure;
    public bool  InContact      => _state == State.Contact;
    public bool  ProbeReady     => _probe != null && _probe.Ready;
    public const float MaxClosure = MaxContactClosure;
    public const float ProbeRange = ProbeOutRange;

    PlayerController _pc;
    RefuelProbe      _probe;
    TankerDrogueRig  _rig;
    float _prevDist = -1f, _closure, _bounceT, _fullT, _lowFuelT;
    Vector3 _err;   // 바스켓(현재) − 프로브 끝, 급유기 좌표계 (x 우, y 위, z 앞)

    // ── HUD ──────────────────────────────────────────────────────────────
    static readonly Color HG   = new Color(0.20f, 1.00f, 0.55f, 0.95f);
    static readonly Color INK  = new Color(0.90f, 1.00f, 0.95f, 0.96f);
    static readonly Color DIM  = new Color(0.70f, 0.80f, 0.76f, 0.80f);
    static readonly Color WARN = new Color(0.96f, 0.72f, 0.29f, 1.00f);
    static readonly Color CRIT = new Color(0.94f, 0.36f, 0.41f, 1.00f);
    static readonly Color GLASS = new Color(0.02f, 0.05f, 0.08f, 0.62f);

    Canvas _canvas;
    RectTransform _canvasRT, _panel, _reticle;
    Text _title, _l1, _l2, _l3, _reticleLabel;
    Image _reticleImg, _bar;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }
    void Start() => BuildHud();

    void Update()
    {
        var gm = GameModeManager.Instance;
        PlayerController pc = gm != null && gm.LocalAircraft != null ? gm.LocalAircraft.GetComponent<PlayerController>() : null;
        bool flying = pc != null && gm.IsFlying && !gm.IsDead && pc.enabled;
        if (pc != _pc) { Disconnect(null); _pc = pc; _probe = pc != null ? pc.GetComponent<RefuelProbe>() : null; }
        if (!flying || _probe == null)
        {
            if (_state == State.Contact) Disconnect(null);
            if (_probe != null) _probe.Extended = false;
            _state = State.Off;
            ShowHud(false);
            return;
        }

        _rig = TankerDrogueRig.Nearest(_pc.transform.position, out float tankerDist);
        HasTanker = _rig != null && _rig.transform != _pc.transform;
        if (!HasTanker)
        {
            TankerDistance = float.MaxValue;
            _probe.Extended = false; _state = State.Off; ShowHud(false);
            return;
        }
        TankerDistance = tankerDist;
        TankerBearing  = Bearing(_rig.transform.position);
        TankerAltDiff  = _rig.transform.position.y - _pc.transform.position.y;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        _bounceT -= dt;
        _probe.Extended = tankerDist < ProbeOutRange || _state == State.Contact;

        Transform tk = _rig.transform;
        Vector3 tip = _probe.TipWorld;
        Vector3 basket = _rig.BasketWorld;
        _err = Quaternion.Inverse(tk.rotation) * (basket - tip);
        float dist = (basket - tip).magnitude;
        _closure = _prevDist < 0f ? 0f : Mathf.Lerp(_closure, (_prevDist - dist) / dt, 1f - Mathf.Exp(-dt * 5f));
        _prevDist = dist;

        if (_state == State.Contact) UpdateContact(dt);
        else
        {
            _state = tankerDist > ProbeOutRange ? State.Rendezvous : State.Precontact;
            if (_probe.Ready && dist < CaptureRadius && _bounceT <= 0f)
            {
                if (_closure <= MaxContactClosure) Connect();
                else
                {
                    _bounceT = 1.5f;
                    Notice("TOO FAST — BACK OFF", true);
                    SfxPlayer.Play2D(FlightSounds.HitMetal, 0.3f, 0.7f);
                }
            }
        }

        // 연료 부족 — 급유기 방향 안내 (30초마다)
        _lowFuelT -= dt;
        if (_pc.IsFuelLow && _state != State.Contact && tankerDist > 1500f && _lowFuelT <= 0f)
        {
            _lowFuelT = 30f;
            Notice($"LOW FUEL  ·  TANKER {Bearing(tk.position):000}°  {tankerDist / 1000f:F1} km  ALT {(tk.position.y - FXLib.SeaY):F0} m", true);
        }

        UpdateHud(tankerDist, tk);
    }

    void UpdateContact(float dt)
    {
        // 바스켓은 프로브에 물려 있으므로 허용 범위는 '호스가 원래 늘어진 자리' 기준으로 판단
        Transform tk = _rig.transform;
        Vector3 rel = Quaternion.Inverse(tk.rotation) * (_probe.TipWorld - _rig.RestBasketWorld);
        bool outLat  = Mathf.Abs(rel.x) > LateralEnvelope || Mathf.Abs(rel.y) > LateralEnvelope;
        bool outFore = rel.z > PushInMax;
        bool outAft  = rel.z < -PullBackMax;
        if (outLat || outFore || outAft)
        {
            Disconnect(outAft ? (_pc.IsFuelFull ? "DISCONNECT  ·  FUEL FULL" : "DISCONNECT") : "BREAKAWAY — OUT OF ENVELOPE");
            return;
        }

        if (!_pc.IsFuelFull)
        {
            _pc.AddFuel(FlowRate * dt);
            _rig.Flowing = true;
            _fullT = 0f;
        }
        else
        {
            _rig.Flowing = false;
            _fullT += dt;
            if (_fullT > 0.05f && _fullT - dt <= 0.05f) Notice("FUEL FULL — 감속해서 빠지세요");
            if (_fullT > 6f) Disconnect("DISCONNECT  ·  FUEL FULL");
        }
    }

    void Connect()
    {
        _state = State.Contact;
        _fullT = 0f;
        _rig.Attach(_probe);
        _pc.RefuelContact = true;
        SfxPlayer.Play2D(FlightSounds.HitMetal, 0.35f, 0.55f);   // 커플링이 물리는 둔탁한 '텅'
        Notice("CONTACT  ·  FUEL FLOWING");
    }

    void Disconnect(string msg)
    {
        if (_rig != null && _rig.Attached == _probe) { _rig.Attach(null); _rig.Flowing = false; }
        if (_pc != null) _pc.RefuelContact = false;
        if (_state == State.Contact)
        {
            _state = State.Precontact;
            _bounceT = 1f;
            SfxPlayer.Play2D(FlightSounds.SwitchClick, 0.5f, 0.6f);
            if (msg != null) Notice(msg, msg.StartsWith("BREAK"));
        }
    }

    float Bearing(Vector3 p)
    {
        Vector3 d = p - _pc.transform.position;
        return Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
    }

    static void Notice(string m, bool warn = false) => CombatFeedUI.Instance?.ShowNotice(m, warn);

    // ════════════════════════════════════════════════════════════════════════
    // HUD
    // ════════════════════════════════════════════════════════════════════════
    void BuildHud()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("[AAR HUD]");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 40;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        _canvasRT = (RectTransform)go.transform;

        // 하단 중앙 패널
        _panel = NewRect("Panel", _canvasRT);
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0f);
        _panel.pivot = new Vector2(0.5f, 0f);
        _panel.anchoredPosition = new Vector2(0f, 150f);
        _panel.sizeDelta = new Vector2(430f, 118f);
        var bg = _panel.gameObject.AddComponent<Image>();
        bg.sprite = HudSprites.Get(HudSprites.Panel); bg.type = Image.Type.Sliced; bg.color = GLASS; bg.raycastTarget = false;

        _title = NewText("Title", _panel, font, 17, FontStyle.Bold, HG, new Vector2(0f, -16f));
        _l1 = NewText("L1", _panel, font, 15, FontStyle.Normal, INK, new Vector2(0f, -42f));
        _l2 = NewText("L2", _panel, font, 15, FontStyle.Normal, INK, new Vector2(0f, -64f));
        _l3 = NewText("L3", _panel, font, 15, FontStyle.Normal, DIM, new Vector2(0f, -86f));

        var barBg = NewRect("FuelBarBg", _panel);
        barBg.anchorMin = barBg.anchorMax = new Vector2(0.5f, 0f);
        barBg.anchoredPosition = new Vector2(0f, 10f); barBg.sizeDelta = new Vector2(380f, 5f);
        var bbi = barBg.gameObject.AddComponent<Image>(); bbi.color = new Color(1f, 1f, 1f, 0.12f); bbi.raycastTarget = false;
        var bar = NewRect("FuelBar", barBg);
        bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f); bar.pivot = new Vector2(0f, 0.5f);
        bar.anchoredPosition = Vector2.zero; bar.sizeDelta = new Vector2(380f, 0f);
        _bar = bar.gameObject.AddComponent<Image>(); _bar.color = HG; _bar.raycastTarget = false;

        // 바스켓 레티클
        _reticle = NewRect("DrogueReticle", _canvasRT);
        _reticle.anchorMin = _reticle.anchorMax = new Vector2(0.5f, 0.5f);
        _reticle.sizeDelta = new Vector2(46f, 46f);
        _reticleImg = _reticle.gameObject.AddComponent<Image>();
        _reticleImg.sprite = HudSprites.Get(HudSprites.RingThin); _reticleImg.raycastTarget = false;
        _reticleLabel = NewText("Label", _reticle, font, 13, FontStyle.Bold, HG, new Vector2(0f, -36f));
        _reticleLabel.rectTransform.anchorMin = _reticleLabel.rectTransform.anchorMax = new Vector2(0.5f, 0f);

        ShowHud(false);
    }

    static RectTransform NewRect(string n, Transform parent)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Text NewText(string n, RectTransform parent, Font font, int size, FontStyle style, Color col, Vector2 pos)
    {
        var rt = NewRect(n, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(410f, 22f);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = style; t.color = col;
        t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
        var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.6f); sh.effectDistance = new Vector2(1f, -1f);
        return t;
    }

    void ShowHud(bool on)
    {
        if (_canvas != null && _canvas.enabled != on) _canvas.enabled = on;
    }

    void UpdateHud(float tankerDist, Transform tk)
    {
        bool show = tankerDist < HudRange || _state == State.Contact || (_pc.IsFuelLow && tankerDist < 25000f);
        ShowHud(show);
        if (!show) return;

        // 훈련 안내 막대(FlightCoach)가 같은 하단 중앙을 쓰므로 떠 있는 동안은 그 위로 비켜 선다
        float wantY = FlightCoach.BarShown ? 318f : 150f;
        var ap = _panel.anchoredPosition;
        _panel.anchoredPosition = new Vector2(0f, Mathf.MoveTowards(ap.y, wantY, Time.unscaledDeltaTime * 900f));

        float fuel01 = _pc.CurrentFuel / Mathf.Max(1f, _pc.MaxFuel);
        _bar.rectTransform.sizeDelta = new Vector2(380f * fuel01, 0f);
        _bar.color = fuel01 < 0.2f ? WARN : HG;
        float altDiff = tk.position.y - _pc.transform.position.y;
        float spdDiff = (_rig.Velocity.magnitude - _pc.CurrentSpeed) * 3.6f;

        switch (_state)
        {
            case State.Rendezvous:
                _title.text = "AAR  ·  RENDEZVOUS"; _title.color = HG;
                _l1.text = $"TANKER  {tankerDist / 1000f:F1} km   BRG {Bearing(tk.position):000}°   ALT {(altDiff >= 0 ? "+" : "")}{altDiff:F0} m";
                _l2.text = $"TANKER SPD {_rig.Velocity.magnitude * 3.6f:F0} km/h   Δ {(spdDiff >= 0 ? "+" : "")}{spdDiff:F0}";
                _l2.color = INK;
                _l3.text = $"FUEL {_pc.CurrentFuel:F0} / {_pc.MaxFuel:F0}   ·   뒤쪽 아래에서 접근";
                break;
            case State.Precontact:
            {
                bool slow = _closure > MaxContactClosure, fast = _closure > MaxContactClosure * 0.7f;
                _title.text = _probe.Ready ? "AAR  ·  PRECONTACT" : "AAR  ·  PROBE EXTENDING";
                _title.color = HG;
                _l1.text = $"DROGUE {_prevDist:F1} m   CLOSURE {(_closure >= 0 ? "+" : "")}{_closure:F1} m/s";
                _l1.color = slow ? CRIT : fast ? WARN : INK;
                _l2.text = $"{(_err.y >= 0 ? "▲" : "▼")} {Mathf.Abs(_err.y):F1} m    {(_err.x >= 0 ? "▶" : "◀")} {Mathf.Abs(_err.x):F1} m    SPD Δ {(spdDiff >= 0 ? "+" : "")}{spdDiff:F0} km/h";
                _l2.color = Mathf.Abs(_err.x) < 0.8f && Mathf.Abs(_err.y) < 0.8f ? HG : INK;
                _l3.text = _prevDist > 30f ? "급유기와 속도를 맞추고 바스켓 뒤로" : $"접근속도 1~{MaxContactClosure:F0} m/s 로 프로브를 꽂으세요";
                break;
            }
            case State.Contact:
            {
                Vector3 rel = Quaternion.Inverse(tk.rotation) * (_probe.TipWorld - _rig.RestBasketWorld);
                float margin = Mathf.Min(LateralEnvelope - Mathf.Max(Mathf.Abs(rel.x), Mathf.Abs(rel.y)),
                                         Mathf.Min(PushInMax - rel.z, rel.z + PullBackMax));
                _title.text = _pc.IsFuelFull ? "AAR  ·  FUEL FULL" : "AAR  ·  CONTACT — FUEL FLOWING";
                _title.color = HG;
                _l1.text = $"FUEL {_pc.CurrentFuel:F0} / {_pc.MaxFuel:F0}   {(_pc.IsFuelFull ? "" : $"+{FlowRate:F0} /s")}";
                _l1.color = INK;
                _l2.text = $"POSITION  {(rel.y >= 0 ? "▲" : "▼")} {Mathf.Abs(rel.y):F1}   {(rel.x >= 0 ? "▶" : "◀")} {Mathf.Abs(rel.x):F1}   HOSE {rel.z:+0.0;-0.0} m";
                _l2.color = margin < 0.6f ? WARN : HG;
                _l3.text = _pc.IsFuelFull ? "감속해서 바스켓에서 빠지세요" : "급유기와 같은 속도로 자세 유지";
                break;
            }
        }

        // 바스켓 레티클 — 350m 이내, 화면 안에 있을 때
        var cam = Camera.main;
        bool ret = cam != null && _prevDist < 350f && _state != State.Rendezvous;
        if (ret)
        {
            Vector3 sp = cam.WorldToScreenPoint(_rig.BasketWorld);
            ret = sp.z > 0f;
            if (ret)
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, sp, null, out local);
                _reticle.anchoredPosition = local;
                float s = Mathf.Clamp(900f / Mathf.Max(_prevDist, 1f), 28f, 90f);
                _reticle.sizeDelta = new Vector2(s, s);
                Color c = _state == State.Contact ? HG : _prevDist < 5f ? (_closure > MaxContactClosure ? CRIT : HG) : INK;
                _reticleImg.color = c;
                _reticleLabel.color = c;
                _reticleLabel.text = _state == State.Contact ? "" : $"DROGUE {_prevDist:F0} m";
            }
        }
        _reticle.gameObject.SetActive(ret);
    }
}
