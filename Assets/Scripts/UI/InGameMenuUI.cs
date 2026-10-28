using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// GameScene 의 빈 GameObject 에 붙입니다.
/// ESC 키로 일시정지 메뉴를 열고 닫습니다. (UITheme 기반, 시안 7번 PAUSE 패널)
/// 기존 동작 유지: 전투기 탑승 중 ESC 차단, 커서 표시/잠금, RETURN TO BASE = 룸 나가기 후 로비 이동.
/// </summary>
public class InGameMenuUI : MonoBehaviour
{
    Font uiFont;
    Canvas canvas;
    CanvasGroup overlayGroup;
    RectTransform panel;
    Text pilotText;
    Text zoneText;
    bool menuOpen;
    Coroutine fadeCo;
    UIThemedButton returnBtn;

    // 조작 설정 패널 (v4.1 F-4)
    RectTransform controlsPanel;
    Text sensText, deadzoneText, deviceText;
    UIThemedButton invertBtn;
    readonly UIThemedButton[] bindBtns = new UIThemedButton[SimInput.ActionCount];
    int captureIdx = -1;
    static readonly KeyCode[] s_allKeys = (KeyCode[])System.Enum.GetValues(typeof(KeyCode));

    void Awake()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Start()
    {
        BuildUI();
        SetMenuVisible(false, true);
    }

    void Update()
    {
        if (captureIdx >= 0) { CaptureKey(); return; }
        if (menuOpen && controlsPanel.gameObject.activeSelf) RefreshDevice();
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        var gm = GameModeManager.Instance;
        // 프리플라이트(콕핏 탑승, 비행 전) 중 ESC 는 하차 — 메뉴 차단.
        // 비행 중에는 조작 설정을 위해 열 수 있지만 룸 나가기는 막는다 (게임은 멈추지 않는다)
        if (gm != null && gm.IsBoardedInCockpit && !gm.IsFlying) return;
        // 확인 모달이 떠 있으면 모달이 ESC를 처리한다
        if (menuOpen && FindAnyObjectByType<UIModal>() != null) return;
        if (menuOpen && controlsPanel.gameObject.activeSelf) { ShowControls(false); return; }
        SetMenuVisible(!menuOpen);
    }

    void SetMenuVisible(bool visible, bool instant = false)
    {
        menuOpen = visible;
        SimInput.Suppressed = visible;
        if (visible) RefreshInfo();
        if (!visible && controlsPanel != null) { captureIdx = -1; controlsPanel.gameObject.SetActive(false); panel.gameObject.SetActive(true); }

        // 커서 표시/숨김
        Cursor.visible   = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;

        if (fadeCo != null) StopCoroutine(fadeCo);
        if (instant || !isActiveAndEnabled)
        {
            overlayGroup.alpha = visible ? 1f : 0f;
            canvas.gameObject.SetActive(visible);
        }
        else fadeCo = StartCoroutine(FadeCo(visible));
    }

    IEnumerator FadeCo(bool show)
    {
        float d = UITheme.Instance.panelTransition;
        canvas.gameObject.SetActive(true);
        overlayGroup.blocksRaycasts = show;
        float from = overlayGroup.alpha, to = show ? 1f : 0f;
        Vector2 end = Vector2.zero;
        for (float e = 0f; e < d; e += Time.unscaledDeltaTime)
        {
            float u = 1f - Mathf.Pow(1f - Mathf.Clamp01(e / d), 3f);
            overlayGroup.alpha = Mathf.Lerp(from, to, u);
            panel.anchoredPosition = end + Vector2.down * (1f - overlayGroup.alpha) * 12f;
            yield return null;
        }
        overlayGroup.alpha = to;
        panel.anchoredPosition = end;
        if (!show) canvas.gameObject.SetActive(false);
        fadeCo = null;
    }

    void RefreshInfo()
    {
        string pilot = GameManager.Instance?.myNickname ?? "UNKNOWN";
        int    zone  = GameManager.Instance?.currentRoomId ?? 0;
        if (pilotText != null) pilotText.text = pilot.ToUpper();
        if (zoneText  != null) zoneText.text  = zone > 0 ? $"ZONE {zone:D2}" : "ZONE —";
        bool flying = GameModeManager.Instance != null && GameModeManager.Instance.IsFlying;
        if (returnBtn != null) returnBtn.Interactable = !flying;   // 비행 중에는 착함 후 나가기
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────
    void BuildUI()
    {
        var t = UITheme.Instance;

        var canvasGO = new GameObject("PauseCanvas");
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        overlayGroup = canvasGO.AddComponent<CanvasGroup>();

        // 현재 비행/지상 화면을 유지한 채 어둡게 — 뒤쪽 클릭 차단
        var overlay = NewRect("Overlay", canvasGO.transform);
        Stretch(overlay);
        var ov = overlay.gameObject.AddComponent<Image>();
        ov.color = new Color(0.01f, 0.03f, 0.06f, 0.72f);
        ov.raycastTarget = true;

        // 중앙 패널
        const float W = 460f, H = 590f;
        panel = NewRect("Panel", canvasGO.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(W, H);
        panel.gameObject.AddComponent<Image>().color = new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.94f);
        var po = panel.gameObject.AddComponent<Outline>();
        po.effectColor = new Color(t.border.r, t.border.g, t.border.b, 0.9f);
        po.effectDistance = new Vector2(1.5f, 1.5f);

        var bar = NewRect("AccentBar", panel);
        bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1); bar.sizeDelta = new Vector2(0, 3);
        var barImg = bar.gameObject.AddComponent<Image>();
        barImg.color = t.accentCyan; barImg.raycastTarget = false;

        // 제목
        MakeText(panel, "PAUSE", t.titleM, FontStyle.Bold, t.textPrimary, TextAnchor.MiddleCenter,
                 new Vector2(0, H * 0.5f - 56f), new Vector2(W - 60f, 44f));
        MakeText(panel, "M I S S I O N   P A U S E D", t.textXS, FontStyle.Normal, t.accentCyan,
                 TextAnchor.MiddleCenter, new Vector2(0, H * 0.5f - 92f), new Vector2(W - 60f, 18f));

        // 파일럿 정보 카드
        var info = NewRect("InfoCard", panel);
        info.anchorMin = info.anchorMax = new Vector2(0.5f, 0.5f);
        info.anchoredPosition = new Vector2(0, H * 0.5f - 160f);
        info.sizeDelta = new Vector2(W - 80f, 64f);
        var infoImg = info.gameObject.AddComponent<Image>();
        infoImg.color = new Color(0f, 0f, 0f, 0.28f); infoImg.raycastTarget = false;
        MakeText(info, "PILOT", t.textXS, FontStyle.Bold, t.textSecondary, TextAnchor.MiddleLeft,
                 new Vector2(-(W - 80f) * 0.5f + 70f, 10f), new Vector2(110f, 18f));
        pilotText = MakeText(info, "—", t.textM, FontStyle.Bold, t.textPrimary, TextAnchor.MiddleLeft,
                 new Vector2(-(W - 80f) * 0.5f + 70f + 0f, -12f), new Vector2(W - 120f, 22f));
        zoneText = MakeText(info, "ZONE —", t.textS, FontStyle.Bold, t.accentCyan, TextAnchor.MiddleRight,
                 new Vector2((W - 80f) * 0.5f - 90f, 0f), new Vector2(150f, 22f));
        // 좌측 정렬 보정: 텍스트 박스를 왼쪽 기준으로 맞춘다
        AlignLeft(pilotText.rectTransform, 16f); AlignLeft(info.GetChild(0) as RectTransform, 16f);

        // 버튼
        float bw = W - 80f, bh = t.buttonHeightL;
        float y0 = H * 0.5f - 250f;
        var cont = MakeButton(panel, "▶   CONTINUE", UIThemedButton.Style.Primary,
                              new Vector2(0, y0), new Vector2(bw, bh));
        cont.OnClick = () => SetMenuVisible(false);

        var ctl = MakeButton(panel, "FLIGHT CONTROLS", UIThemedButton.Style.Secondary,
                             new Vector2(0, y0 - bh - 14f), new Vector2(bw, bh));
        ctl.OnClick = () => ShowControls(true);

        var ret = MakeButton(panel, "RETURN TO BASE", UIThemedButton.Style.Secondary,
                             new Vector2(0, y0 - 2f * (bh + 14f)), new Vector2(bw, bh));
        ret.OnClick = OnReturnToBaseClick;
        returnBtn = ret;

        BuildControlsPanel(canvasGO.transform);

        MakeText(panel, "[ ESC ]  메뉴 열기 / 닫기", t.textXS, FontStyle.Normal, t.textDisabled,
                 TextAnchor.MiddleCenter, new Vector2(0, -H * 0.5f + 28f), new Vector2(W, 18f));
    }

    // ── 조작 설정 패널 ───────────────────────────────────────────────────
    void ShowControls(bool show)
    {
        captureIdx = -1;
        controlsPanel.gameObject.SetActive(show);
        panel.gameObject.SetActive(!show);
        if (show) RefreshControls();
    }

    void BuildControlsPanel(Transform parent)
    {
        var t = UITheme.Instance;
        const float W = 640f, H = 860f;
        controlsPanel = NewRect("ControlsPanel", parent);
        controlsPanel.anchorMin = controlsPanel.anchorMax = new Vector2(0.5f, 0.5f);
        controlsPanel.sizeDelta = new Vector2(W, H);
        controlsPanel.gameObject.AddComponent<Image>().color = new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.96f);
        var po = controlsPanel.gameObject.AddComponent<Outline>();
        po.effectColor = new Color(t.border.r, t.border.g, t.border.b, 0.9f);
        po.effectDistance = new Vector2(1.5f, 1.5f);
        var bar = NewRect("AccentBar", controlsPanel);
        bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1); bar.sizeDelta = new Vector2(0, 3);
        bar.gameObject.AddComponent<Image>().color = t.accentCyan;

        float y = H * 0.5f - 44f;
        MakeText(controlsPanel, "FLIGHT CONTROLS", t.titleM - 4, FontStyle.Bold, t.textPrimary, TextAnchor.MiddleCenter,
                 new Vector2(0, y), new Vector2(W - 60f, 36f));
        y -= 34f;
        deviceText = MakeText(controlsPanel, "", t.textS, FontStyle.Bold, t.accentCyan, TextAnchor.MiddleCenter,
                 new Vector2(0, y), new Vector2(W - 60f, 20f));

        float lx = -W * 0.5f + 40f, colW = W - 80f;
        y -= 44f;
        sensText = SettingRow("MOUSE SENSITIVITY", y, lx, colW,
            () => { SimInput.MouseSensitivity = Mathf.Max(0.3f, Mathf.Round((SimInput.MouseSensitivity - 0.1f) * 10f) / 10f); SimInput.Save(); RefreshControls(); },
            () => { SimInput.MouseSensitivity = Mathf.Min(3f,   Mathf.Round((SimInput.MouseSensitivity + 0.1f) * 10f) / 10f); SimInput.Save(); RefreshControls(); });
        y -= 40f;
        deadzoneText = SettingRow("STICK DEADZONE", y, lx, colW,
            () => { SimInput.StickDeadzone = Mathf.Max(0f,   Mathf.Round((SimInput.StickDeadzone - 0.02f) * 100f) / 100f); SimInput.Save(); RefreshControls(); },
            () => { SimInput.StickDeadzone = Mathf.Min(0.3f, Mathf.Round((SimInput.StickDeadzone + 0.02f) * 100f) / 100f); SimInput.Save(); RefreshControls(); });
        y -= 40f;
        MakeLeftText(controlsPanel, "INVERT PITCH", t.textS, t.textSecondary, lx, y);
        invertBtn = MakeButton(controlsPanel, "OFF", UIThemedButton.Style.Outline, new Vector2(lx + colW - 70f, y), new Vector2(140f, 30f));
        invertBtn.OnClick = () => { SimInput.InvertPitch = !SimInput.InvertPitch; SimInput.Save(); RefreshControls(); };

        y -= 30f;
        var div = NewRect("Divider", controlsPanel);
        div.anchorMin = div.anchorMax = new Vector2(0.5f, 0.5f);
        div.anchoredPosition = new Vector2(0, y); div.sizeDelta = new Vector2(colW, 1f);
        div.gameObject.AddComponent<Image>().color = t.border;
        y -= 22f;
        MakeText(controlsPanel, "KEY BINDINGS  —  클릭 후 새 키 입력 (ESC 취소)", t.textXS, FontStyle.Normal, t.textDisabled,
                 TextAnchor.MiddleCenter, new Vector2(0, y), new Vector2(colW, 18f));

        y -= 30f;
        for (int i = 0; i < SimInput.ActionCount; i++)
        {
            int idx = i;
            MakeLeftText(controlsPanel, SimInput.Label((SimAction)i), t.textS, t.textSecondary, lx, y);
            var b = MakeButton(controlsPanel, "", UIThemedButton.Style.Outline, new Vector2(lx + colW - 90f, y), new Vector2(180f, 28f));
            b.OnClick = () => { captureIdx = idx; RefreshControls(); };
            bindBtns[i] = b;
            y -= 31f;
        }

        var reset = MakeButton(controlsPanel, "RESET DEFAULTS", UIThemedButton.Style.Ghost,
                               new Vector2(-colW * 0.25f - 6f, -H * 0.5f + 40f), new Vector2(colW * 0.5f - 12f, 40f));
        reset.OnClick = () => { SimInput.ResetToDefaults(); RefreshControls(); };
        var back = MakeButton(controlsPanel, "BACK", UIThemedButton.Style.Primary,
                              new Vector2(colW * 0.25f + 6f, -H * 0.5f + 40f), new Vector2(colW * 0.5f - 12f, 40f));
        back.OnClick = () => ShowControls(false);

        controlsPanel.gameObject.SetActive(false);
    }

    Text SettingRow(string label, float y, float lx, float colW, System.Action dec, System.Action inc)
    {
        var t = UITheme.Instance;
        MakeLeftText(controlsPanel, label, t.textS, t.textSecondary, lx, y);
        float rx = lx + colW;
        var minus = MakeButton(controlsPanel, "-", UIThemedButton.Style.Outline, new Vector2(rx - 125f, y), new Vector2(30f, 30f));
        minus.OnClick = dec;
        var val = MakeText(controlsPanel, "", t.textM, FontStyle.Bold, t.textPrimary, TextAnchor.MiddleCenter,
                           new Vector2(rx - 72f, y), new Vector2(76f, 30f));
        var plus = MakeButton(controlsPanel, "+", UIThemedButton.Style.Outline, new Vector2(rx - 17f, y), new Vector2(30f, 30f));
        plus.OnClick = inc;
        return val;
    }

    void MakeLeftText(RectTransform parent, string content, int fs, Color col, float x, float y)
    {
        var txt = MakeText(parent, content, fs, FontStyle.Bold, col, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(320f, 26f));
        var rt = txt.rectTransform;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
    }

    void RefreshControls()
    {
        sensText.text     = $"{SimInput.MouseSensitivity:F1}x";
        deadzoneText.text = $"{SimInput.StickDeadzone * 100f:F0}%";
        invertBtn.SetLabel(SimInput.InvertPitch ? "ON" : "OFF");
        for (int i = 0; i < bindBtns.Length; i++)
            bindBtns[i].SetLabel(i == captureIdx ? "PRESS KEY..." : KeyName(SimInput.KeyFor((SimAction)i)));
        RefreshDevice();
    }

    void RefreshDevice()
    {
        string dev = SimInput.ControllerName;
        deviceText.text = dev != null ? $"CONTROLLER  >  {dev.ToUpper()}  +  KEYBOARD / MOUSE" : "CONTROLLER  >  KEYBOARD / MOUSE";
    }

    void CaptureKey()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { captureIdx = -1; RefreshControls(); return; }
        if (!Input.anyKeyDown) return;
        foreach (var k in s_allKeys)
        {
            if (k == KeyCode.None || k == KeyCode.Escape) continue;
            if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) continue;   // 버튼 클릭과 겹침
            if (!Input.GetKeyDown(k)) continue;
            SimInput.Rebind((SimAction)captureIdx, k);
            captureIdx = -1;
            RefreshControls();
            return;
        }
    }

    static string KeyName(KeyCode k) => k switch
    {
        KeyCode.LeftShift => "L-SHIFT", KeyCode.RightShift => "R-SHIFT",
        KeyCode.LeftControl => "L-CTRL", KeyCode.LeftAlt => "L-ALT",
        KeyCode.BackQuote => "BACKQUOTE", KeyCode.Space => "SPACE",
        _ => k.ToString().ToUpper(),
    };

    void OnReturnToBaseClick()
    {
        UIModal.Show(canvas.transform, "로비로 돌아가시겠습니까?",
                     "현재 구역에서 나가 로비로 이동합니다.", "확인", "취소",
                     OnReturnToBase, null, true);
    }

    void OnReturnToBase()
    {
        Cursor.visible   = true;
        Cursor.lockState = CursorLockMode.None;
        NetworkManager.Instance?.socketClient.LeaveRoom();
        StartCoroutine(GoToLobby());
    }

    IEnumerator GoToLobby()
    {
        yield return new WaitForSeconds(0.2f);
        SceneManager.LoadScene("LobbyScene");
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────
    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void AlignLeft(RectTransform rt, float x)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, rt.anchorMin.y);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    }

    Text MakeText(RectTransform parent, string content, int fs, FontStyle style, Color color,
                  TextAnchor anchor, Vector2 pos, Vector2 size)
    {
        var rt = NewRect("T", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var txt = rt.gameObject.AddComponent<Text>();
        txt.text = content; txt.font = uiFont; txt.fontSize = fs; txt.fontStyle = style;
        txt.color = color; txt.alignment = anchor; txt.raycastTarget = false;
        return txt;
    }

    UIThemedButton MakeButton(RectTransform parent, string label, UIThemedButton.Style style,
                              Vector2 pos, Vector2 size)
    {
        var t = UITheme.Instance;
        var rt = NewRect("Btn_" + label, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        rt.gameObject.AddComponent<Image>();
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectDistance = new Vector2(1.5f, 1.5f);
        var txt = MakeText(rt, label, t.textM + 1, FontStyle.Bold, t.textPrimary,
                           TextAnchor.MiddleCenter, Vector2.zero, size);
        Stretch(txt.rectTransform);
        var btn = rt.gameObject.AddComponent<UIThemedButton>();
        btn.ButtonStyle = style;
        return btn;
    }
}
