using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공통 확인/취소 모달. 오버레이 Fade In → 패널 등장, 닫을 때 역순.
/// 사용: UIModal.Show(canvasTransform, "제목", "내용", "확인", "취소", () => { ... });
/// cancelLabel 을 null 로 주면 확인 버튼만 표시한다.
/// </summary>
public class UIModal : MonoBehaviour
{
    CanvasGroup _overlay;
    RectTransform _panel;
    CanvasGroup _panelGroup;
    bool _closing;
    Action _onCancel;

    public static UIModal Show(Transform canvas, string title, string message,
                               string confirmLabel, string cancelLabel,
                               Action onConfirm, Action onCancel = null, bool danger = false)
    {
        var t = UITheme.Instance;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var root = new GameObject("UIModal");
        root.transform.SetParent(canvas, false);
        var rrt = root.AddComponent<RectTransform>();
        Stretch(rrt);
        root.transform.SetAsLastSibling();

        var modal = root.AddComponent<UIModal>();
        modal._onCancel = onCancel;

        // 오버레이 — 뒤쪽 입력 차단
        var ovImg = root.AddComponent<Image>();
        ovImg.color = new Color(0.01f, 0.03f, 0.07f, 0.72f);
        ovImg.raycastTarget = true;
        modal._overlay = root.AddComponent<CanvasGroup>();
        modal._overlay.alpha = 0f;

        // 패널 (그림자 → 본체 → 테두리)
        float w = 540f, h = string.IsNullOrEmpty(message) ? 230f : 280f;
        var panel = NewRect("Panel", root.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(w, h);
        modal._panel = panel;
        modal._panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        modal._panelGroup.alpha = 0f;

        Color accent = danger ? t.error : t.primaryBlue;
        UIShapes.AddPanelSkin(panel, 16,
            new Color(t.panelBG.r * 0.9f, t.panelBG.g * 0.9f, t.panelBG.b * 0.95f, 0.98f),
            new Color(t.border.r + 0.04f, t.border.g + 0.06f, t.border.b + 0.1f, 0.95f));

        // 상단 은은한 하이라이트 (유리 질감)
        var gloss = UIBackdropFit.MakeGradient(panel, "Gloss", false, new Color(accent.r, accent.g, accent.b), 0f, 0.07f);
        gloss.anchorMin = new Vector2(0.02f, 0.45f); gloss.anchorMax = new Vector2(0.98f, 0.99f);
        gloss.offsetMin = gloss.offsetMax = Vector2.zero;

        // 닫기 (✕) = 취소
        var closeRT = NewRect("Close", panel);
        closeRT.anchorMin = closeRT.anchorMax = new Vector2(1f, 1f);
        closeRT.pivot = new Vector2(1f, 1f);
        closeRT.anchoredPosition = new Vector2(-18f, -18f);
        closeRT.sizeDelta = new Vector2(34f, 34f);
        closeRT.gameObject.AddComponent<Image>();
        MakeText(closeRT, "✕", font, t.textM, FontStyle.Normal, t.textSecondary,
                 TextAnchor.MiddleCenter, Vector2.zero, new Vector2(34f, 34f));
        Stretch(closeRT.GetChild(0) as RectTransform);
        var closeBtn = closeRT.gameObject.AddComponent<UIThemedButton>();
        closeBtn.ButtonStyle = UIThemedButton.Style.Ghost;
        closeBtn.OnClick = () => modal.Close(modal._onCancel);

        // 제목 / 본문
        MakeText(panel, title, font, t.textL + 4, FontStyle.Bold, t.textPrimary,
                 TextAnchor.MiddleCenter, new Vector2(0, h * 0.5f - 66f), new Vector2(w - 120f, 40f));
        if (!string.IsNullOrEmpty(message))
            MakeText(panel, message, font, t.textM, FontStyle.Normal, t.textSecondary,
                     TextAnchor.MiddleCenter, new Vector2(0, 14f), new Vector2(w - 100f, 60f));

        // 버튼
        float by = -h * 0.5f + 56f;
        bool hasCancel = !string.IsNullOrEmpty(cancelLabel);
        float bw = hasCancel ? 210f : 280f, bh = t.buttonHeightM + 6f;
        var okBtn = MakeButton(panel, confirmLabel, font, danger ? UIThemedButton.Style.Danger : UIThemedButton.Style.Primary,
                               new Vector2(hasCancel ? -bw * 0.5f - 8f : 0f, by), new Vector2(bw, bh));
        okBtn.OnClick = () => modal.Close(onConfirm);
        if (hasCancel)
        {
            var cancel = MakeButton(panel, cancelLabel, font, UIThemedButton.Style.Secondary,
                                    new Vector2(bw * 0.5f + 8f, by), new Vector2(bw, bh));
            cancel.OnClick = () => modal.Close(modal._onCancel);
        }

        modal.StartCoroutine(modal.OpenCo());
        return modal;
    }

    public void Close(Action after = null)
    {
        if (_closing) return;           // 빠른 연속 클릭으로 중복 실행 방지
        _closing = true;
        StartCoroutine(CloseCo(after));
    }

    IEnumerator OpenCo()
    {
        var t = UITheme.Instance;
        float d = t.panelTransition;
        Vector2 end = _panel.anchoredPosition;
        for (float e = 0f; e < d; e += Time.unscaledDeltaTime)
        {
            float u = EaseOut(e / d);
            _overlay.alpha = u;
            _panelGroup.alpha = u;
            _panel.anchoredPosition = end + Vector2.down * (1f - u) * 14f;
            yield return null;
        }
        _overlay.alpha = _panelGroup.alpha = 1f;
        _panel.anchoredPosition = end;
    }

    IEnumerator CloseCo(Action after)
    {
        _overlay.blocksRaycasts = false;
        float d = UITheme.Instance.panelTransition * 0.7f;
        for (float e = 0f; e < d; e += Time.unscaledDeltaTime)
        {
            float u = 1f - e / d;
            _overlay.alpha = u;
            yield return null;
        }
        after?.Invoke();
        Destroy(gameObject);
    }

    void Update()
    {
        // ESC = 취소
        if (!_closing && Input.GetKeyDown(KeyCode.Escape))
            Close(_onCancel);
    }

    static float EaseOut(float u) { u = Mathf.Clamp01(u); return 1f - Mathf.Pow(1f - u, 3f); }

    // ── 헬퍼 ──────────────────────────────────────────────────────────────
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static void MakeText(Transform parent, string content, Font font, int size, FontStyle style,
                         Color color, TextAnchor anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var rt = NewRect("Text", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var tx = rt.gameObject.AddComponent<Text>();
        tx.font = font; tx.fontSize = size; tx.fontStyle = style;
        tx.color = color; tx.alignment = anchor; tx.text = content;
        tx.raycastTarget = false;
    }

    static UIThemedButton MakeButton(Transform parent, string label, Font font,
                                     UIThemedButton.Style style, Vector2 pos, Vector2 size)
    {
        var t = UITheme.Instance;
        var rt = NewRect("Btn_" + label, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        rt.gameObject.AddComponent<Image>();
        MakeText(rt, label, font, t.textM, FontStyle.Bold, t.textPrimary,
                 TextAnchor.MiddleCenter, Vector2.zero, size);
        var txtRT = rt.GetChild(0) as RectTransform;
        Stretch(txtRT);
        var btn = rt.gameObject.AddComponent<UIThemedButton>();
        btn.ButtonStyle = style;
        return btn;
    }
}
