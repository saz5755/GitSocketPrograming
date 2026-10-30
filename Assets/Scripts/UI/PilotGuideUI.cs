using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조종사 가이드 (조작법·무장·위협 회피 설명서) — 비행 교관 NPC 대화, ESC 메뉴 "PILOT GUIDE" 에서 연다.
/// 런타임에 스스로 생성되는 싱글턴 (씬 배치 불필요). 키 이름은 SimInput 현재 바인딩을 그대로 읽어
/// 사용자가 키를 바꿔도 설명이 맞게 표시된다.
/// 하단 "실전 훈련" 버튼으로 FlightCoach(단계별 체크리스트 + 위협 상황 코칭)를 켜고 끈다.
/// </summary>
public class PilotGuideUI : MonoBehaviour
{
    public static PilotGuideUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;
    /// <summary>이번 프레임에 ESC 로 닫혔는가 — 같은 ESC 로 일시정지 메뉴가 열리지 않게</summary>
    public static bool ClosedThisFrame => s_closedFrame == Time.frameCount;
    static int s_closedFrame = -1;

    static readonly string[] Tabs = { "1  지상 · 탑승 · 출격", "2  비행 조작", "3  무장 사용", "4  위협 인지 · 회피", "5  착함 · 피격 · 기타", "6  연료 · 급유" };

    Font _font;
    GameObject _root;
    Text _title, _body;
    readonly UIThemedButton[] _tabBtns = new UIThemedButton[Tabs.Length];
    UIThemedButton _trainBtn, _tipsBtn;
    int _page;
    GroundController _frozenGc;
    bool _prevCursorVisible;
    CursorLockMode _prevLock;

    /// <summary>가이드 열기 (없으면 생성)</summary>
    public static void Open(int page = 0)
    {
        if (Instance == null) new GameObject("PilotGuide").AddComponent<PilotGuideUI>();
        Instance.Show(page);
    }

    public static void Close() { if (Instance != null) Instance.Hide(); }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        _root.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }
        for (int i = 0; i < Tabs.Length; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetPage(i);
    }

    // ── 열기 / 닫기 ───────────────────────────────────────────────────────
    void Show(int page)
    {
        if (!IsOpen)
        {
            _prevCursorVisible = Cursor.visible; _prevLock = Cursor.lockState;
            // 지상 캐릭터 이동·시점 정지 (비행 중에는 게임이 멈추지 않으므로 비행 입력만 막는다)
            var gc = GameModeManager.Instance?.GroundCharacter;
            _frozenGc = gc != null ? gc.GetComponent<GroundController>() : null;
            if (_frozenGc != null && _frozenGc.enabled) _frozenGc.enabled = false; else _frozenGc = null;
        }
        _root.SetActive(true);
        SimInput.Suppressed = true;
        Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        SetPage(page);
        RefreshTrainButton();
    }

    void Hide()
    {
        if (!IsOpen) return;
        s_closedFrame = Time.frameCount;
        _root.SetActive(false);
        SimInput.Suppressed = false;
        if (_frozenGc != null) { _frozenGc.enabled = true; _frozenGc = null; }   // OnEnable 이 커서를 다시 잠근다
        else { Cursor.visible = _prevCursorVisible; Cursor.lockState = _prevLock; }
    }

    void SetPage(int i)
    {
        _page = Mathf.Clamp(i, 0, Tabs.Length - 1);
        for (int k = 0; k < _tabBtns.Length; k++) _tabBtns[k].Selected = k == _page;
        _title.text = Tabs[_page].Substring(3);
        _body.text  = PageText(_page);
    }

    void ToggleTraining()
    {
        if (FlightCoach.TrainingActive) FlightCoach.StopTraining();
        else { FlightCoach.StartTraining(); Hide(); }
        RefreshTrainButton();
    }

    void RefreshTrainButton()
    {
        _trainBtn?.SetLabel(FlightCoach.TrainingActive ? "■  실전 훈련 중지" : "▶  실전 훈련 시작 (단계별 코치)");
        _tipsBtn?.SetLabel(FlightCoach.ThreatTipsEnabled ? "위협 코칭  ON" : "위협 코칭  OFF");
    }

    // ── 키 이름 ───────────────────────────────────────────────────────────
    public static string K(SimAction a) => KeyName(SimInput.KeyFor(a));

    public static string KeyName(KeyCode k) => k switch
    {
        KeyCode.Space      => "SPACE",
        KeyCode.LeftShift  => "L-SHIFT",
        KeyCode.RightShift => "R-SHIFT",
        KeyCode.LeftControl => "L-CTRL",
        KeyCode.BackQuote  => "`",
        KeyCode.Mouse0     => "마우스 좌클릭",
        KeyCode.Mouse1     => "마우스 우클릭",
        KeyCode.Mouse2     => "휠 클릭",
        >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)(k - KeyCode.Alpha0)).ToString(),
        _                  => k.ToString().ToUpper(),
    };

    const string KeyCol = "#46D7F2", WarnCol = "#FFB84D", CritCol = "#FF5C66", OkCol = "#33FF8C";
    static string Key(string k)  => $"<color={KeyCol}><b>[{k}]</b></color>";
    static string Key(SimAction a) => Key(K(a));
    static string H(string s)    => $"<color={WarnCol}><b>{s}</b></color>";
    static string Crit(string s) => $"<color={CritCol}><b>{s}</b></color>";
    static string Ok(string s)   => $"<color={OkCol}><b>{s}</b></color>";

    // ── 페이지 내용 ───────────────────────────────────────────────────────
    static string PageText(int page)
    {
        bool combat = !RoomModeSO.IsAirshow;
        switch (page)
        {
            case 0: return
$@"{H("지상 이동")}
  {Key("W A S D")} 이동    {Key("L-SHIFT")} 달리기    {Key("SPACE")} 점프    {Key("마우스")} 시점 회전

{H("탑승")}
  항모 갑판의 전투기에 8m 이내로 다가가면 원형 표시가 뜹니다 → {Key("F")} 콕핏 탑승

{H("시동 (프리플라이트)")}
  콕핏 스위치를 {Key("마우스 좌클릭")} 으로 순서대로 켭니다:  APU → ENG(엔진) → AV(항전장비)
  출격 전 {Key("ESC")} 는 하차입니다.

{H("출격")}
  모든 스위치가 켜지면 {Key("F")} → 카타펄트 발진.  발진 직후 {Key(SimAction.ThrottleUp)} 를 누르고 있어야 속도가 유지됩니다.

{H("언제든지")}
  {Key("ESC")} 메뉴 (FLIGHT CONTROLS 에서 키 재지정·마우스 감도·조이스틱 설정)
  {Key("`")} (홀드) 스코어보드" + (combat ? "" : "\n\n" + Ok("에어쇼 모드") + " — 무장은 꺼져 있고 플레어는 연출용으로 쓸 수 있습니다.");

            case 1: return
$@"{H("기본 조종")}
  {Key("마우스 ↑↓")} 피치(기수 올림/내림)        {Key("마우스 ←→")} 요(기수 좌우)
  {Key(SimAction.RollLeft)} / {Key(SimAction.RollRight)} 롤 (좌/우 기울기) — 기울인 뒤 기수를 당기면 선회합니다

{H("추력")}
  {Key(SimAction.ThrottleUp)} 홀드 = 밀리터리 추력 (놓으면 아이들)      {Key(SimAction.Afterburner)} 애프터버너 (연료 소모 큼, 열 신호 증가)
  {Key(SimAction.ThrottleDown)} 스피드 브레이크

{H("기동")}
  {Key(SimAction.BarrelRollLeft)} / {Key(SimAction.BarrelRollRight)} 홀드 = 배럴롤 + 플레어 자동 연사 (미사일 회피용)
  {Key(SimAction.Immelmann)} 줌 클라임 → 다시 누르면 임멜만 반전

{H("시점")}
  {Key("마우스 우클릭")} 홀드 = 자유 시점 (그동안 조종 입력 정지)
  {Key(SimAction.ViewToggle)} 콕핏 / 3인칭 전환    {Key(SimAction.TargetCam)} 홀드 = 표적 캠    {Key(SimAction.MissileCam)} 미사일 캠

{H("주의")}
  급선회는 G 가 쌓입니다 — {Crit("OVER G")} 가 뜨면 잠시 풀어 주세요 (계속하면 블랙아웃).
  속도가 너무 낮으면 {Crit("STALL")} — 기수를 낮추고 추력을 올리세요.
  조이스틱·HOTAS·게임패드를 연결하면 자동으로 인식합니다.";

            case 2: return combat ?
$@"{H("기총")}  {Key("마우스 좌클릭")} 홀드 — 사거리 약 700m
  적기 앞에 표시되는 리드(예측) 마커에 크로스헤어를 맞추고 짧게 끊어 쏩니다.

{H("무장 선택")}  {Key(SimAction.WeaponSelect)} — AIM-9X ↔ AIM-120 (HUD 무장 카드의 ▶ 표시)

{H("AIM-9X 적외선 미사일 (단거리 ~3km)")}
  1. 기수를 적기 쪽(전방 20° 안)으로 돌리면 낮은 {Ok("그르릉 톤")} — 시커가 열원을 봄
  2. 잠시 유지하면 높은 {Ok("삐— 락 톤")} — 포착 완료
  3. {Key(SimAction.FireMissile)} 발사.  레이더 락이 필요 없고 상대에게 락 경보도 가지 않습니다.
     꼬리(엔진 쪽)를 볼수록 빨리 포착됩니다.

{H("AIM-120 레이더 미사일 (중거리 ~7km)")}
  1. {Key(SimAction.LockTarget)} 레이더 락 시작 → 약 2.5초 후 LOCK  (다시 누르면 표적 전환/해제)
  2. {Key(SimAction.FireMissile)} 발사 → 미사일이 가까이 갈 때까지 표적을 기수 60° 안에 두세요 (데이터링크)
  3. 가까워지면 미사일이 스스로 레이더를 켜고(피트불) 유도합니다.

{H("HUD 발사 큐")}
  {Crit("SHOOT")} 상대가 도망쳐도 맞는 거리 — 지금 쏘세요       {Ok("IN RNG")} 사거리 안 (상대가 회피하면 빗나갈 수 있음)
  {H("OUT RNG")} 너무 멂      {H("MIN RNG")} 너무 가까움      {H("STEER")} 기수를 표적 쪽으로 더 돌리세요
  Rmax / Rne 숫자는 현재 표적에 대한 최대 사거리 / 무탈출 사거리입니다."
            :
$@"{H("에어쇼 모드")}
  이 방에서는 기총·미사일·락온이 비활성화되어 있습니다.
  무장 교전은 방을 만들 때 COMBAT 모드를 선택하세요.

  플레어 {Key(SimAction.Flare)} / 채프 {Key(SimAction.Chaff)} 는 연출용으로 사용할 수 있습니다.";

            case 3: return
$@"{H("RWR (레이더 경보 수신기) — 콕핏 좌측 원형 스코프 / 화면 상단 경고 배너")}
  바늘이 가리키는 방향 = 위협 방향 (위 = 기수 정면)
  {Ok("TRK")}  적 레이더가 나를 탐지 · 추적 중                — 느린 비프음
  {H("LCK")}  적이 나를 {H("락온")} — 곧 미사일이 날아옵니다         — 빠른 비프음
  {Crit("MSL")}  미사일 발사됨 / 접근 중                         — 고저 경보음 + 화면 붉은 테두리 + ""MISSILE"" 음성
  {Crit("ARH")}  레이더 미사일이 스스로 유도 중 (가장 위험)     — 고주파 연속 경보음
  ※ 적의 적외선 미사일은 {H("락 단계 경보가 없습니다")} — 적이 꼬리에 붙으면 미리 플레어를 뿌리세요.

{H("미사일 회피 절차")}
  1. RWR 바늘로 미사일 방향을 확인
  2. {H("빔 기동")}: 미사일 방향 기준 {H("측면 90°")} 로 급선회 (미사일이 계속 방향을 꺾게 만듦)
  3. 대응책 — 적외선(열추적)엔 {Key(SimAction.Flare)} 플레어, 레이더엔 {Key(SimAction.Chaff)} 채프. 모르겠으면 둘 다!
     {Key(SimAction.BarrelRollLeft)}/{Key(SimAction.BarrelRollRight)} 홀드 배럴롤은 플레어를 자동으로 연사합니다.
  4. {H("타이밍")}: 미사일이 약 2km 안에 왔을 때 2~3발씩 — 너무 일찍 뿌리면 플레어가 꺼진 뒤 다시 추적합니다.
  5. 플레어를 뿌릴 땐 {Key(SimAction.Afterburner)} 애프터버너를 끄면 내 열 신호가 줄어 효과가 커집니다.
  6. 레이더 미사일: 측면 비행(노치) + 채프가 가장 잘 통합니다. 멀리서 오면 {H("등을 돌려 도망")} — 미사일 연료가 바닥납니다.
  7. 마지막 순간(수백 m) 급선회 — 미사일은 회전 반경이 커서 따라오지 못합니다.";

            case 5: return
$@"{H("연료")}
  연료는 SYSTEMS 카드의 FUEL 로 확인합니다.  {Key(SimAction.Afterburner)} 애프터버너는 평소의 약 4배를 씁니다.
  20% 아래면 {H("BINGO FUEL")} 경고와 함께 급유기 방향을 알려 줍니다.  0% 가 되면 {Crit("FUEL EMPTY")} — 엔진 추력이 끊겨 활공합니다.

{H("① 항모 갑판 급유")}
  착함 후 다시 전투기에 탑승하면 {H("시동 절차(APU → ENG → AV)를 하는 동안 갑판 요원이 연료를 채웁니다")} (약 6초).
  발진할 때는 항상 만탱크입니다.

{H("② 공중급유 (프로브-드로그)")}  — 비행을 멈추지 않고 하늘에서 연료를 받습니다
  1. {H("랑데부")}: HUD 의 {Ok("TANKER")} 마커(화면 밖이면 화살표)를 따라 급유기로 — 항모 북동쪽, 고도 약 1,500m, 약 245 km/h
  2. {H("프로브 전개")}: 1.2km 안에 들어오면 기수 오른쪽 급유 프로브가 자동으로 펴집니다
  3. {H("프리컨택")}: 급유기 뒤 아래에 늘어진 {H("바스켓")} 뒤 수 m 에 자리 잡고 속도를 맞춥니다
     하단 AAR 패널 — ▲▼ ◀▶ = 바스켓이 있는 쪽,  SPD Δ = 급유기와의 속도 차
  4. {H("컨택")}: {Ok("접근속도 1~3 m/s")} 로 천천히 밀어 넣어 프로브 끝을 바스켓에 꽂습니다
     너무 빠르면 {Crit("TOO FAST")} — 튕겨 나옵니다.  성공하면 {Ok("CONTACT")} + 급유기 포드 표시등이 {Ok("녹색")}
  5. {H("급유")}: 급유기와 같은 속도로 자세 유지 (좌우·상하 약 2m 안).  벗어나면 {Crit("BREAKAWAY")} 분리
  6. {H("분리")}: 만탱크가 되면 {Key(SimAction.ThrottleDown)} 로 살짝 감속해 뒤로 빠집니다

  표시등:  {H("황색")} = 급유 준비   {Ok("녹색")} = 연료 이송 중";

            default: return
$@"{H("항모 착함")}
  항모 뒤쪽에서 정렬해 감속 접근 → 착함 존에서 갑판에 내리면 어레스팅 와이어가 잡아 줍니다.
  너무 빠르면 {Crit("WAVE OFF")} — 다시 돌아 접근하세요.   정지 후(20 KPH 미만) {Key("F")} 하차

{H("피격 · 손상")}
  피격되면 화면 가장자리 손상 표시와 경고가 뜹니다. 엔진 손상 시 최고 속도가 떨어집니다.
  {Crit("CRITICAL DAMAGE")} 면 이탈하거나 사출을 준비하세요.
  사출: {Key("L-CTRL")} + {Key("E")} 를 연속 3회
  격추 후에는 안내가 뜨면 {Key("F")} 로 재출격

{H("ESC 메뉴")}
  FLIGHT CONTROLS — 모든 키 재지정, 마우스 감도·피치 반전, 조이스틱 데드존
  PILOT GUIDE     — 이 가이드
  ENEMY AI        — (COMBAT 방, 방장만) 적기 숙련도 ROOKIE / VETERAN / ACE

{H("훈련")}
  아래 ""실전 훈련 시작"" 을 누르면 화면 좌측에 단계별 체크리스트가 뜨고,
  락온·미사일 경보가 울릴 때 무엇을 해야 하는지 바로 알려 줍니다.";
        }
    }

    // ── UI 구성 ───────────────────────────────────────────────────────────
    void Build()
    {
        var t = UITheme.Instance;
        _root = new GameObject("PilotGuideCanvas");
        _root.transform.SetParent(transform, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;   // ESC 메뉴(50) 위
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        var overlay = Rect("Overlay", _root.transform, Vector2.zero, Vector2.zero);
        overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
        overlay.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.03f, 0.06f, 0.78f);

        const float W = 1240f, Hh = 760f;
        var panel = Rect("Panel", _root.transform, Vector2.zero, new Vector2(W, Hh));
        panel.gameObject.AddComponent<Image>().color = new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.97f);
        var o = panel.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(t.border.r, t.border.g, t.border.b, 0.9f); o.effectDistance = new Vector2(1.5f, 1.5f);
        var bar = Rect("Accent", panel, new Vector2(0f, Hh * 0.5f - 1.5f), new Vector2(W, 3f));
        bar.gameObject.AddComponent<Image>().color = t.accentCyan;

        Txt(panel, "PILOT GUIDE", new Vector2(-W * 0.5f + 40f, Hh * 0.5f - 44f), new Vector2(400f, 40f), 30, t.textPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
        Txt(panel, "비행 교관 브리핑  ·  키 설명은 현재 키 설정 기준", new Vector2(-W * 0.5f + 40f, Hh * 0.5f - 76f), new Vector2(600f, 20f), 13, t.accentCyan, TextAnchor.MiddleLeft);

        // 좌측 탭
        for (int i = 0; i < Tabs.Length; i++)
        {
            int idx = i;
            var b = Button(panel, Tabs[i], UIThemedButton.Style.Outline, new Vector2(-W * 0.5f + 175f, Hh * 0.5f - 140f - i * 62f), new Vector2(270f, 50f), 15);
            b.OnClick = () => SetPage(idx);
            _tabBtns[i] = b;
        }

        // 본문
        float bx = -W * 0.5f + 340f;
        _title = Txt(panel, "", new Vector2(bx, Hh * 0.5f - 130f), new Vector2(820f, 34f), 22, t.textPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
        var sep = Rect("Sep", panel, new Vector2(bx + 430f, Hh * 0.5f - 152f), new Vector2(860f, 1f));
        sep.gameObject.AddComponent<Image>().color = new Color(t.border.r, t.border.g, t.border.b, 0.8f);
        _body = Txt(panel, "", new Vector2(bx, Hh * 0.5f - 168f), new Vector2(860f, 500f), 16, t.textPrimary, TextAnchor.UpperLeft);
        var brt = _body.rectTransform; brt.pivot = new Vector2(0f, 1f);
        _body.supportRichText = true; _body.lineSpacing = 1.15f;
        _body.horizontalOverflow = HorizontalWrapMode.Wrap; _body.verticalOverflow = VerticalWrapMode.Overflow;

        // 하단 버튼
        _trainBtn = Button(panel, "", UIThemedButton.Style.Primary, new Vector2(W * 0.5f - 470f, -Hh * 0.5f + 46f), new Vector2(380f, 52f), 16);
        _trainBtn.OnClick = ToggleTraining;
        _tipsBtn = Button(panel, "", UIThemedButton.Style.Outline, new Vector2(W * 0.5f - 790f, -Hh * 0.5f + 46f), new Vector2(220f, 52f), 15);
        _tipsBtn.OnClick = () => { FlightCoach.ThreatTipsEnabled = !FlightCoach.ThreatTipsEnabled; RefreshTrainButton(); };
        var close = Button(panel, "닫기  [ESC]", UIThemedButton.Style.Secondary, new Vector2(W * 0.5f - 150f, -Hh * 0.5f + 46f), new Vector2(220f, 52f), 16);
        close.OnClick = Hide;
        Txt(panel, $"[1]~[{Tabs.Length}] 탭 이동", new Vector2(-W * 0.5f + 40f, -Hh * 0.5f + 46f), new Vector2(300f, 20f), 12, t.textSecondary, TextAnchor.MiddleLeft);
    }

    static RectTransform Rect(string n, Transform p, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(p, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Text Txt(Transform p, string s, Vector2 pos, Vector2 size, int fs, Color c, TextAnchor a, FontStyle st = FontStyle.Normal)
    {
        var rt = Rect("T", p, pos, size);
        if (a == TextAnchor.MiddleLeft || a == TextAnchor.UpperLeft) rt.pivot = new Vector2(0f, 0.5f);
        var tx = rt.gameObject.AddComponent<Text>();
        tx.font = _font; tx.text = s; tx.fontSize = fs; tx.color = c; tx.alignment = a; tx.fontStyle = st;
        tx.horizontalOverflow = HorizontalWrapMode.Overflow; tx.verticalOverflow = VerticalWrapMode.Overflow;
        tx.raycastTarget = false;
        return tx;
    }

    UIThemedButton Button(RectTransform parent, string label, UIThemedButton.Style style, Vector2 pos, Vector2 size, int fs)
    {
        var rt = Rect("Btn", parent, pos, size);
        rt.gameObject.AddComponent<Image>();
        var o = rt.gameObject.AddComponent<Outline>(); o.effectDistance = new Vector2(1.5f, 1.5f);
        var tx = Txt(rt, label, Vector2.zero, size, fs, UITheme.Instance.textPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
        tx.rectTransform.anchorMin = Vector2.zero; tx.rectTransform.anchorMax = Vector2.one;
        tx.rectTransform.offsetMin = tx.rectTransform.offsetMax = Vector2.zero;
        var b = rt.gameObject.AddComponent<UIThemedButton>();
        b.ButtonStyle = style;
        return b;
    }
}
