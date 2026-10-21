using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로그인 씬 UI — Premium Military Flight Simulator 디자인.
/// 코드만으로 블루 그라데이션 하늘 + 글래스 로그인 패널 + 대형 KF-21 타이포 구현.
/// 모든 색상/간격은 UITheme (Resources/UITheme.asset)에서 중앙 관리.
/// </summary>
public class LoginUI : MonoBehaviour
{
    Font uiFont;

    InputField ipField;
    InputField idField;
    InputField pwField;
    Text       statusText;
    UIThemedButton loginBtn;

    void Awake()
    {
        EnsureManagers();
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        UIStartup.SetCameraBackground(Camera.main);
    }

    void Start()
    {
        BuildUI();
        SubscribeEvents();
        StartCoroutine(UIAssets.Prewarm(uiFont));    // 로비 진입 시 끊김 방지: 연출이 끝난 뒤 한가할 때 예열
    }

    void OnDestroy()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnLoginResult -= HandleLoginResult;
    }

    void EnsureManagers()
    {
        if (GameManager.Instance == null)
            new GameObject("[GameManager]").AddComponent<GameManager>();
        if (NetworkManager.Instance == null)
            new GameObject("[NetworkManager]").AddComponent<NetworkManager>();
        if (UnityMainThreadDispatcher.Instance == null)
            new GameObject("[Dispatcher]").AddComponent<UnityMainThreadDispatcher>();
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    void SubscribeEvents()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnLoginResult += HandleLoginResult;
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────
    const float LeftMargin = 150f;   // 로그인 영역 좌측 여백 (1920 기준)
    const float ColW       = 440f;   // 로그인 영역 폭

    Transform _canvasRoot;
    LoginJetFX _jetFx;
    InputField[] _fields;

    void BuildUI()
    {
        var t = UITheme.Instance;

        var canvasGO = new GameObject("LoginCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        _canvasRoot = canvasGO.transform;

        BuildBackground(canvasGO.transform, t);
        BuildLoginColumn(canvasGO.transform, t);
        BuildTopBar(canvasGO.transform, t);
        BuildFooter(canvasGO.transform, t);

        _fields = new[] { ipField, idField, pwField };
    }

    // ── 1. 배경: KF-21 비행장 이미지 + 좌측 어두운 그라데이션 ──────────────
    void BuildBackground(Transform parent, UITheme t)
    {
        // 최하단 단색 (이미지가 못 덮는 영역 대비)
        var baseRT = MakeImage(parent, "BaseColor", Vector2.zero, Vector2.zero, t.primaryBG);
        Stretch(baseRT);

        var hero = MakeEmpty(parent, "Hero", Vector2.zero, Vector2.zero);
        Stretch(hero);

        // 우측 정렬, 높이에 맞춰 폭 결정 (화면 비율이 달라도 비행기가 잘리지 않도록)
        var tex = UIAssets.LoginBg;
        if (tex != null)
        {
            var imgGO = new GameObject("BG_Photo");
            imgGO.transform.SetParent(hero, false);
            imgGO.AddComponent<RectTransform>();
            var raw = imgGO.AddComponent<RawImage>();
            raw.texture = tex; raw.raycastTarget = false;
            // 원본 비율 유지 (어떤 해상도에서도 늘어나지 않도록 직접 계산)
            imgGO.AddComponent<UIBackdropFit>().Configure(UIBackdropFit.Mode.FitHeightRight, tex.width / (float)tex.height);

            // 사진 좌측 경계를 배경색으로 녹여 이음새가 보이지 않게 한다
            var edge = MakeRawGradient(imgGO.transform, "EdgeBlend", true, t.primaryBG, 1f, 0f);
            edge.anchorMin = Vector2.zero; edge.anchorMax = new Vector2(0.55f, 1f);
            edge.offsetMin = edge.offsetMax = Vector2.zero;

            // 인증 중 연출 (윤곽 코팅 스캔 → 후방 점화)
            _jetFx = imgGO.AddComponent<LoginJetFX>();   // 텍스처/레이어는 첫 Play() 때 지연 생성
        }
        else
        {
            // 이미지가 없을 때의 폴백 — 기존 그라데이션
            var sky = MakeImage(hero, "SkyGradient", Vector2.zero, Vector2.zero, Color.white);
            Stretch(sky);
            var skyImg = sky.GetComponent<Image>();
            skyImg.sprite = CreateVerticalGradient(HexC("#030810"), HexC("#0A1B2D"), HexC("#1A3B5F"));
        }

        // 좌→우 어두운 그라데이션 (로그인 입력 가독성)
        var side = MakeRawGradient(hero, "SideFade", true, t.primaryBG, 1f, 0f);
        side.anchorMin = Vector2.zero; side.anchorMax = new Vector2(0.78f, 1f);
        side.offsetMin = side.offsetMax = Vector2.zero;

        // 하단 어둡게 (푸터 가독성)
        var bottom = MakeRawGradient(hero, "BottomFade", false, new Color(0.01f, 0.03f, 0.06f), 0.85f, 0f);
        bottom.anchorMin = Vector2.zero; bottom.anchorMax = new Vector2(1f, 0.22f);
        bottom.offsetMin = bottom.offsetMax = Vector2.zero;

        // 상단 어둡게 (상단 바 가독성)
        var top = MakeRawGradient(hero, "TopFade", false, new Color(0.01f, 0.03f, 0.06f), 0f, 0.7f);
        top.anchorMin = new Vector2(0f, 0.82f); top.anchorMax = Vector2.one;
        top.offsetMin = top.offsetMax = Vector2.zero;

        // 전체 톤 다운
        var tone = MakeImage(hero, "Tone", Vector2.zero, Vector2.zero, new Color(0.02f, 0.06f, 0.12f, 0.18f));
        Stretch(tone);

        // 배경 줌 애니메이션이 다른 UI 까지 매 프레임 재배칭하지 않도록 별도 캔버스로 분리
        hero.gameObject.AddComponent<Canvas>();
        hero.gameObject.AddComponent<UIDrift>();
        hero.gameObject.AddComponent<UIFadeIn>().Configure(0f, t.heroTransition + 0.2f, 0f);
    }

    // ── 2. 좌측 로그인 컬럼: 로고 → 카피 → 입력 → 버튼 ─────────────────────
    void BuildLoginColumn(Transform parent, UITheme t)
    {
        // ── 로고 블록
        var logo = MakeLeftBlock(parent, "LogoBlock", LeftMargin, 290f, 520f, 240f);
        var bigLogo = AddLeftText(logo, "KF-21", new Vector2(0f, -70f), new Vector2(520f, 130f), 124,
                    Color.white, FontStyle.BoldAndItalic);
        UIType.Gradient(bigLogo, UIType.Ice, UIType.Steel, new Color(0.42f, 0.60f, 1f));
        UIType.Glow(bigLogo, UIType.Cyan, 4f, 0.20f);
        // 로고 밑을 가로지르는 빛줄기 (시안 스윕)
        UIType.Swoosh(logo, new Vector2(-40f, -122f), new Vector2(600f, 34f), new Color(0.30f, 0.72f, 1f, 0.55f));
        var boram = AddLeftText(logo, UIType.Track("BORAMAE", 2), new Vector2(8f, -158f), new Vector2(520f, 50f), 40,
                    Color.white, FontStyle.Italic);
        UIType.Gradient(boram, UIType.Ice, new Color(0.80f, 0.88f, 1f), new Color(0.55f, 0.68f, 0.95f));
        UIType.Glow(boram, UIType.Blue, 2f, 0.18f);
        var sub = AddLeftText(logo, UIType.Track("FLIGHT COMBAT SIMULATION", 1), new Vector2(8f, -204f),
                    new Vector2(520f, 22f), t.textXS, new Color(0.62f, 0.74f, 0.92f, 0.85f), FontStyle.Normal);
        // 하단 강조선 (시안 → 투명) + 짧은 앰버 포인트
        UIType.AccentLine(logo, new Vector2(8f, -234f), 320f, 2f, t.accentCyan);
        UIType.AccentLine(logo, new Vector2(8f, -234f), 34f, 3f, new Color(1f, 0.62f, 0.28f), 1f, 1f);
        logo.gameObject.AddComponent<UIFadeIn>().Configure(0.25f, t.heroTransition, 18f);

        // ── 카피
        var copy = MakeLeftBlock(parent, "CopyBlock", LeftMargin, 28f, ColW, 80f);
        var fly = AddLeftText(copy, "Fly Beyond the Sky", new Vector2(0f, -22f), new Vector2(ColW, 36f), 28,
                    Color.white, FontStyle.Normal);
        UIType.Gradient(fly, UIType.Ice, new Color(0.9f, 0.95f, 1f), new Color(0.7f, 0.82f, 1f));
        AddLeftText(copy, "대한민국의 하늘, 당신의 손으로.", new Vector2(0f, -56f), new Vector2(ColW, 24f), t.textM - 1,
                    new Color(t.primaryBlue.r + 0.15f, t.primaryBlue.g + 0.15f, t.primaryBlue.b, 0.85f), FontStyle.Normal);
        copy.gameObject.AddComponent<UIFadeIn>().Configure(0.40f, t.screenTransition, 14f);

        // ── 입력 + 버튼
        var form = MakeLeftBlock(parent, "FormBlock", LeftMargin, -60f, ColW, 400f);
        _form = form;
        bool saveId = PlayerPrefs.GetInt(PrefSaveId, 0) == 1;
        ipField = BuildField(form, "SERVER", "서버 IP", PlayerPrefs.GetString(PrefServerIp, "172.30.201.117"), 0f, false, t);
        idField = BuildField(form, "ID",     "아이디를 입력하세요",   saveId ? PlayerPrefs.GetString(PrefSavedId, "") : "", 62f, false, t);
        pwField = BuildField(form, "PW",     "비밀번호를 입력하세요", "", 124f, true, t);
        BuildPasswordToggle(pwField, t);

        BuildSaveIdToggle(form, saveId, 186f, t);

        statusText = AddLeftText(form, "", new Vector2(0f, -240f), new Vector2(ColW, 22f), t.textS,
                                 t.textSecondary, FontStyle.Normal);

        loginBtn = BuildPrimaryButton(form, "로그인", new Vector2(0f, -268f - t.buttonHeightL * 0.5f),
                                      new Vector2(ColW, t.buttonHeightL), t);
        loginBtn.OnClick = OnLoginClick;

        AddLeftText(form, "ENTER 로그인  ·  TAB 다음 입력", new Vector2(0f, -268f - t.buttonHeightL - 22f),
                    new Vector2(ColW, 18f), t.textXS, t.textDisabled, FontStyle.Normal);
        form.gameObject.AddComponent<UIFadeIn>().Configure(0.55f, t.screenTransition, 14f);
    }

    // ── 입력 보조: 비밀번호 표시 / 아이디 저장 ──────────────────────────────
    const string PrefSaveId   = "KF21_SaveId";
    const string PrefSavedId  = "KF21_SavedId";
    const string PrefServerIp = "KF21_ServerIp";

    RectTransform _form;
    bool _saveId;
    Image _saveIdFill;

    void BuildPasswordToggle(InputField pw, UITheme t)
    {
        var fieldRT = (RectTransform)pw.transform;
        var bg = fieldRT.parent;
        fieldRT.offsetMax = new Vector2(-76f, 0f);     // 토글 자리 확보

        var rt = MakeEmpty(bg, "Btn_ShowPw", new Vector2(-10f, 0f), new Vector2(60f, 32f));
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.gameObject.AddComponent<Image>();
        var label = AddTextFill(rt, "표시", t.textXS + 1, t.textSecondary, FontStyle.Bold);
        var btn = rt.gameObject.AddComponent<UIThemedButton>();
        btn.ButtonStyle = UIThemedButton.Style.Ghost;
        btn.OnClick = () =>
        {
            bool show = pw.contentType == InputField.ContentType.Password;
            pw.contentType = show ? InputField.ContentType.Standard : InputField.ContentType.Password;
            pw.ForceLabelUpdate();
            btn.SetLabel(show ? "숨김" : "표시");
        };
    }

    void BuildSaveIdToggle(RectTransform form, bool initial, float yDown, UITheme t)
    {
        _saveId = initial;
        var row = MakeEmpty(form, "Btn_SaveId", new Vector2(0f, -yDown), new Vector2(170f, 30f));
        row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
        row.pivot = new Vector2(0f, 1f);
        row.gameObject.AddComponent<Image>();

        // 체크 박스 (둥근 테두리 + 선택 시 시안 채움)
        var box = MakeEmpty(row, "Box", new Vector2(2f, 0f), new Vector2(18f, 18f));
        box.anchorMin = box.anchorMax = new Vector2(0f, 0.5f);
        box.pivot = new Vector2(0f, 0.5f);
        var boxImg = box.gameObject.AddComponent<Image>();
        UIShapes.Round(boxImg, 5);
        boxImg.color = new Color(0f, 0f, 0f, 0.35f); boxImg.raycastTarget = false;
        UIShapes.AddBorder(box, 5, new Color(t.border.r + 0.05f, t.border.g + 0.08f, t.border.b + 0.12f, 1f));
        var fill = MakeEmpty(box, "Check", Vector2.zero, new Vector2(-8f, -8f));
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(4f, 4f); fill.offsetMax = new Vector2(-4f, -4f);
        _saveIdFill = fill.gameObject.AddComponent<Image>();
        UIShapes.Round(_saveIdFill, 3);
        _saveIdFill.color = t.accentCyan; _saveIdFill.raycastTarget = false;
        _saveIdFill.enabled = _saveId;

        var lbl = MakeEmpty(row, "Label", new Vector2(30f, 0f), new Vector2(130f, 24f));
        lbl.anchorMin = lbl.anchorMax = new Vector2(0f, 0.5f);
        lbl.pivot = new Vector2(0f, 0.5f);
        AddTextFill(lbl, "아이디 저장", t.textS, t.textSecondary, FontStyle.Normal).alignment = TextAnchor.MiddleLeft;

        var btn = row.gameObject.AddComponent<UIThemedButton>();
        btn.ButtonStyle = UIThemedButton.Style.Ghost;
        btn.OnClick = () => { _saveId = !_saveId; _saveIdFill.enabled = _saveId; };
    }

    // 실패 피드백: 폼 좌우 흔들림
    Coroutine _shakeCo;
    IEnumerator ShakeCo()
    {
        if (_form == null) yield break;
        Vector2 basePos = _form.anchoredPosition;
        const float dur = 0.32f;
        for (float e = 0f; e < dur; e += Time.unscaledDeltaTime)
        {
            float u = e / dur;
            float dx = Mathf.Sin(u * Mathf.PI * 6f) * (1f - u) * 11f;
            _form.anchoredPosition = basePos + new Vector2(dx, 0f);
            yield return null;
        }
        _form.anchoredPosition = basePos;
        _shakeCo = null;
    }

    void ShakeForm()
    {
        if (_shakeCo != null) return;
        _shakeCo = StartCoroutine(ShakeCo());
    }

    void ResetLoginButton()
    {
        loginBtn.SetLabel("로그인");
        loginBtn.Interactable = true;
        _jetFx?.Stop();
    }

    // 좌측 기준(앵커 0,0.5) 블록. x: 좌측 여백, yTop: 세로 중심 기준 상단 y, h: 높이
    RectTransform MakeLeftBlock(Transform parent, string name, float x, float yTop, float w, float h)
    {
        var rt = MakeEmpty(parent, name, new Vector2(x, yTop), new Vector2(w, h));
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 1f);
        return rt;
    }

    // 블록 내부(좌상단 기준) 텍스트. pos.y 는 음수 = 아래
    Text AddLeftText(RectTransform parent, string content, Vector2 pos, Vector2 size,
                     int sz, Color color, FontStyle style)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var tx = go.AddComponent<Text>();
        tx.font = uiFont; tx.fontSize = sz; tx.color = color;
        tx.alignment = TextAnchor.MiddleLeft; tx.fontStyle = style; tx.text = content;
        tx.horizontalOverflow = HorizontalWrapMode.Overflow;
        tx.verticalOverflow   = VerticalWrapMode.Overflow;
        tx.raycastTarget = false;
        return tx;
    }

    // ── 3. 상단 바: 우측 종료 ─────────────────────────────────────────────
    void BuildTopBar(Transform parent, UITheme t)
    {
        var bar = MakeEmpty(parent, "TopBar", Vector2.zero, new Vector2(0f, 80f));
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot     = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(0f, -80f);
        bar.offsetMax = new Vector2(0f, 0f);

        var quit = MakeEmpty(bar, "Btn_Quit", new Vector2(-60f, 0f), new Vector2(110f, 40f));
        quit.anchorMin = quit.anchorMax = new Vector2(1f, 0.5f);
        quit.pivot = new Vector2(1f, 0.5f);
        quit.gameObject.AddComponent<Image>();
        var qo = quit.gameObject.AddComponent<Outline>();
        qo.effectDistance = new Vector2(1f, 1f);
        AddTextFill(quit, "종료", t.textS, t.textSecondary, FontStyle.Normal);
        var qb = quit.gameObject.AddComponent<UIThemedButton>();
        qb.ButtonStyle = UIThemedButton.Style.Ghost;
        qb.OnClick = OnQuitClick;

        bar.gameObject.AddComponent<UIFadeIn>().Configure(0.7f, t.panelTransition, 0f);
    }

    void OnQuitClick()
    {
        UIModal.Show(_canvasRoot, "게임을 종료하시겠습니까?", "진행 중인 작업이 저장되지 않을 수 있습니다.",
                     "확인", "취소", () =>
                     {
#if UNITY_EDITOR
                         UnityEditor.EditorApplication.isPlaying = false;
#else
                         Application.Quit();
#endif
                     });
    }

    void BuildFooter(Transform parent, UITheme t)
    {
        var leftGO = new GameObject("FooterLeft");
        leftGO.transform.SetParent(parent, false);
        var lrt = leftGO.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
        lrt.pivot     = new Vector2(0f, 0f);
        lrt.anchoredPosition = new Vector2(LeftMargin, 36f);
        lrt.sizeDelta = new Vector2(500f, 18f);
        var lt = leftGO.AddComponent<Text>();
        lt.font = uiFont; lt.fontSize = t.textXS;
        lt.color = t.textDisabled; lt.alignment = TextAnchor.MiddleLeft;
        lt.text = "VERSION " + Application.version;
        lt.raycastTarget = false;

        var rightGO = new GameObject("FooterRight");
        rightGO.transform.SetParent(parent, false);
        var rrt = rightGO.AddComponent<RectTransform>();
        rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0f);
        rrt.pivot     = new Vector2(1f, 0f);
        rrt.anchoredPosition = new Vector2(-60f, 36f);
        rrt.sizeDelta = new Vector2(500f, 18f);
        var rt = rightGO.AddComponent<Text>();
        rt.font = uiFont; rt.fontSize = t.textXS;
        rt.color = new Color(t.textPrimary.r, t.textPrimary.g, t.textPrimary.b, 0.55f);
        rt.alignment = TextAnchor.MiddleRight;
        rt.fontStyle = FontStyle.Italic;
        rt.text = "더 멀리, 더 높이  ·  KF-21과 함께";
        rt.raycastTarget = false;

        leftGO.AddComponent<UIFadeIn>().Configure(0.8f, t.panelTransition, 0f);
        rightGO.AddComponent<UIFadeIn>().Configure(0.8f, t.panelTransition, 0f);
    }

    // ── 공통 빌더 ───────────────────────────────────────────────────────
    // 입력 필드: 블록 좌상단 기준 yDown 아래로 배치. 포커스 시 테두리 강조.
    InputField BuildField(RectTransform parent, string name, string placeholder, string initialValue,
                          float yDown, bool password, UITheme t)
    {
        var bg = MakeImage(parent, name + "_BG", new Vector2(0f, -yDown), new Vector2(ColW, 50f),
                           new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.62f));
        bg.anchorMin = bg.anchorMax = new Vector2(0f, 1f);
        bg.pivot = new Vector2(0f, 1f);
        bg.GetComponent<Image>().raycastTarget = true;
        UIShapes.Round(bg.GetComponent<Image>(), 10);
        var outline = UIShapes.AddBorder(bg, 10, new Color(t.border.r, t.border.g, t.border.b, 0.9f));

        var fieldGO = new GameObject(name);
        fieldGO.transform.SetParent(bg.transform, false);
        var frt = fieldGO.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(18f, 0f); frt.offsetMax = new Vector2(-18f, 0f);

        var inputField = fieldGO.AddComponent<InputField>();

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(fieldGO.transform, false);
        var trt = textGO.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var txt = textGO.AddComponent<Text>();
        txt.font = uiFont; txt.fontSize = t.textM;
        txt.color = t.textPrimary; txt.alignment = TextAnchor.MiddleLeft;
        txt.supportRichText = false;

        var placeGO = new GameObject("Placeholder");
        placeGO.transform.SetParent(fieldGO.transform, false);
        var prt = placeGO.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var ptxt = placeGO.AddComponent<Text>();
        ptxt.font = uiFont; ptxt.fontSize = t.textM;
        ptxt.color = t.textSecondary;
        ptxt.alignment = TextAnchor.MiddleLeft;
        ptxt.text = placeholder;

        inputField.textComponent = txt;
        inputField.placeholder   = ptxt;
        inputField.text          = initialValue;
        inputField.contentType   = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
        inputField.caretColor    = t.accentCyan;
        inputField.selectionColor = new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.45f);

        // 포커스 시 테두리/배경 강조
        var accent = MakeEmpty(bg, "FocusBar", new Vector2(0f, 0f), new Vector2(3f, 26f));
        accent.anchorMin = accent.anchorMax = new Vector2(0f, 0.5f);
        accent.pivot = new Vector2(0f, 0.5f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        UIShapes.Round(accentImg, 1);
        accentImg.color = new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0f);
        accentImg.raycastTarget = false;

        fieldGO.AddComponent<UIFieldFocus>().Init(inputField, bg.GetComponent<Image>(), outline, accentImg);

        return inputField;
    }

    UIThemedButton BuildPrimaryButton(RectTransform parent, string label,
                                       Vector2 pos, Vector2 size, UITheme t)
    {
        var btnGO = new GameObject("Btn_" + label);
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        btnGO.AddComponent<Image>();
        var outline = btnGO.AddComponent<Outline>();
        outline.effectDistance = new Vector2(1.5f, 1.5f);

        AddTextFill(rt, label, t.textL, t.textPrimary, FontStyle.Bold);

        var btn = btnGO.AddComponent<UIThemedButton>();
        btn.ButtonStyle = UIThemedButton.Style.Primary;
        return btn;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // 알파 그라데이션 텍스처. horizontal=true: 좌(aStart)→우(aEnd), false: 하(aStart)→상(aEnd)
    RectTransform MakeRawGradient(Transform parent, string name, bool horizontal, Color color,
                                  float aStart, float aEnd)
    {
        const int N = 128;
        var tex = new Texture2D(horizontal ? N : 1, horizontal ? 1 : N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < N; i++)
        {
            float u = i / (float)(N - 1);
            u = u * u * (3f - 2f * u);
            var c = new Color(color.r, color.g, color.b, Mathf.Lerp(aStart, aEnd, u));
            if (horizontal) tex.SetPixel(i, 0, c); else tex.SetPixel(0, i, c);
        }
        tex.Apply();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var raw = go.AddComponent<RawImage>();
        raw.texture = tex; raw.raycastTarget = false;
        return rt;
    }

    // ── 로그인 처리 ──────────────────────────────────────────────────────
    void OnLoginClick()
    {
        string ip = ipField.text.Trim();
        string id = idField.text.Trim();
        string pw = pwField.text.Trim();

        if (string.IsNullOrEmpty(id)) { SetStatus("아이디를 입력하세요", true); ShakeForm(); idField.ActivateInputField(); return; }
        if (string.IsNullOrEmpty(pw)) { SetStatus("비밀번호를 입력하세요", true); ShakeForm(); pwField.ActivateInputField(); return; }

        if (!string.IsNullOrEmpty(ip) && GameManager.Instance != null)
            GameManager.Instance.serverIP = ip;

        SavePrefs(ip, id);
        loginBtn.Interactable = false;
        loginBtn.SetLabel("인증 중…");
        _jetFx?.Play();
        SetStatus("서버에 접속하는 중…", false);

        if (NetworkManager.Instance?.socketClient?.IsConnected() == true)
            NetworkManager.Instance.socketClient.Disconnect();

        try
        {
            NetworkManager.Instance.socketClient.Login(id, pw);
        }
        catch (System.Exception e)
        {
            SetStatus($"접속 실패: {e.Message}", true);
            ResetLoginButton();
            ShakeForm();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab) && _fields != null) FocusNextField();
        if (Input.GetKeyDown(KeyCode.Return) && loginBtn != null && loginBtn.Interactable)
            OnLoginClick();
    }

    void SavePrefs(string ip, string id)
    {
        PlayerPrefs.SetString(PrefServerIp, ip);
        PlayerPrefs.SetInt(PrefSaveId, _saveId ? 1 : 0);
        if (_saveId) PlayerPrefs.SetString(PrefSavedId, id); else PlayerPrefs.DeleteKey(PrefSavedId);
        PlayerPrefs.Save();
    }

    void FocusNextField()
    {
        int cur = -1;
        for (int i = 0; i < _fields.Length; i++)
            if (_fields[i] != null && _fields[i].isFocused) { cur = i; break; }
        int dir = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? -1 : 1;
        int next = cur < 0 ? 1 : (cur + dir + _fields.Length) % _fields.Length;
        _fields[next].ActivateInputField();
    }

    void HandleLoginResult(LoginResultPacket packet)
    {
        if (packet.success)
        {
            SetStatus("인증 성공 — 로비로 이동 중…", false);
            StartCoroutine(LoadLobby());
        }
        else
        {
            SetStatus($"접속 거부 — {packet.message}", true);
            ResetLoginButton();
            ShakeForm();
        }
    }

    IEnumerator LoadLobby()
    {
        // 연출이 도는 동안 로비 씬을 백그라운드로 미리 로드 (활성화는 암전 후)
        var op = SceneManager.LoadSceneAsync("LobbyScene");
        if (op != null) op.allowSceneActivation = false;

        // 점화 연출이 충분히 보이도록 최소 시간 대기 (연출이 없으면 즉시)
        while (_jetFx != null && _jetFx.Playing && _jetFx.Elapsed < 2.4f) yield return null;
        yield return new WaitForSecondsRealtime(0.15f);

        // 암전
        var go = new GameObject("FadeOut");
        go.transform.SetParent(_canvasRoot, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.01f, 0.02f, 0.05f, 1f);
        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        for (float e = 0f; e < 0.35f; e += Time.unscaledDeltaTime)
        {
            cg.alpha = e / 0.35f;
            yield return null;
        }
        cg.alpha = 1f;

        if (op != null)
        {
            while (op.progress < 0.9f) yield return null;   // 사전 로드 완료 대기
            op.allowSceneActivation = true;
        }
        else SceneManager.LoadScene("LobbyScene");
    }

    void SetStatus(string msg, bool warn)
    {
        if (statusText == null) return;
        var t = UITheme.Instance;
        statusText.text  = msg;
        statusText.color = warn ? t.error : t.textSecondary;
    }

    // ── UI 헬퍼 ──────────────────────────────────────────────────────────
    RectTransform MakeImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return rt;
    }

    RectTransform MakeImage(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        => MakeImage((Transform)parent, name, pos, size, color);

    RectTransform MakeEmpty(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Text AddTextCentered(Transform parent, string content, Vector2 pos, Vector2 size,
                          int sz, Color color, FontStyle style)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var tx = go.AddComponent<Text>();
        tx.font = uiFont; tx.fontSize = sz;
        tx.color = color; tx.alignment = TextAnchor.MiddleCenter;
        tx.fontStyle = style; tx.text = content;
        tx.raycastTarget = false;
        return tx;
    }

    Text AddTextFill(Transform parent, string content, int sz, Color color, FontStyle style)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tx = go.AddComponent<Text>();
        tx.font = uiFont; tx.fontSize = sz;
        tx.color = color; tx.alignment = TextAnchor.MiddleCenter;
        tx.fontStyle = style; tx.text = content;
        tx.raycastTarget = false;
        return tx;
    }

    void AddOutline(GameObject target, Color col, float width)
    {
        var o = target.AddComponent<Outline>();
        o.effectColor = col;
        o.effectDistance = new Vector2(width, width);
    }

    // ── Procedural Vertical Gradient (3단: 하단→중간→상단) ────────────────
    static Sprite _cachedGradient;
    Sprite CreateVerticalGradient(Color bottom, Color mid, Color top)
    {
        if (_cachedGradient != null) return _cachedGradient;
        const int H = 256;
        var tex = new Texture2D(2, H, TextureFormat.RGBA32, false, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < H; y++)
        {
            float u = y / (float)(H - 1);
            Color c;
            if (u < 0.5f)
            {
                float a = u / 0.5f;
                a = a * a * (3f - 2f * a); // smoothstep
                c = Color.Lerp(bottom, mid, a);
            }
            else
            {
                float a = (u - 0.5f) / 0.5f;
                a = a * a * (3f - 2f * a);
                c = Color.Lerp(mid, top, a);
            }
            tex.SetPixel(0, y, c);
            tex.SetPixel(1, y, c);
        }
        tex.Apply();
        _cachedGradient = Sprite.Create(tex, new Rect(0, 0, 2, H), new Vector2(0.5f, 0.5f));
        return _cachedGradient;
    }

    static Color HexC(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return Color.magenta;
    }
}
