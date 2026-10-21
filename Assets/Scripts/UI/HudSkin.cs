using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 씬에 이미 저장(베이크)된 HUD 캔버스의 배치/계층은 그대로 두고, 런타임에 시각 품질만 끌어올리는 스킨 패스.
/// (사람이 씬에서 조정한 위치/크기를 덮어쓰지 않기 위해 재빌드 대신 사용한다.)
///
/// 하는 일 (모두 멱등 — 캔버스에 마커가 있으면 다시 하지 않음):
///  1. 이미지에 붙은 Outline → 둥근 테두리 링으로 교체.
///     반투명 이미지에 Outline 을 쓰면 테두리색이 면 전체에 비쳐 "녹색 사각형" 이 되고, 완전 투명 이미지는 아예 안 보이던 문제 해결.
///  2. 콕핏 프레임 패널에 은은한 면 질감(Sheen) + 상단 키라인.
///  3. ENGINE / WEAPON 섹션 뒤에 정보 카드.
///  4. 흐릿했던 라벨/보조선 색(알파 0.50/0.22)을 읽기 좋게(0.66/0.30) 보정.
/// 클래식 녹색 HUD 톤은 유지한다.
/// </summary>
public static class HudSkin
{
    static readonly Color OldHGD = new Color(0.00f, 0.98f, 0.42f, 0.50f);
    static readonly Color OldHGX = new Color(0.00f, 0.98f, 0.42f, 0.22f);
    static readonly Color NewHGD = new Color(0.00f, 0.98f, 0.42f, 0.66f);
    static readonly Color NewHGX = new Color(0.00f, 0.98f, 0.42f, 0.30f);
    static readonly Color Green  = new Color(0.00f, 0.98f, 0.42f, 1f);

    const string Marker = "~HudSkin";

    public static void Apply(Canvas canvas)
    {
        if (canvas == null || canvas.transform.Find(Marker) != null) return;
        var m = new GameObject(Marker, typeof(RectTransform));
        m.transform.SetParent(canvas.transform, false);

        ConvertOutlines(canvas);
        SkinPanels(canvas);
        AddCards(canvas);
        RemapColors(canvas);
    }

    // ── 1. Outline → 링 ──────────────────────────────────────────────────
    static void ConvertOutlines(Canvas canvas)
    {
        foreach (var o in canvas.GetComponentsInChildren<Outline>(true))
        {
            var img = o.GetComponent<Image>();
            if (img == null) continue;                       // 글자(Text)의 Outline 은 외곽선 효과이므로 유지
            var rt = (RectTransform)o.transform;
            Color c = o.effectColor;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);   // 편집 모드 프리뷰에서도 동작

            float w = rt.rect.width, h = rt.rect.height;
            bool clear = img.color.a < 0.05f;
            bool square = Mathf.Abs(w - h) < 1f;
            string n = rt.name;
            bool circle = clear && square && w >= 10f && (n == "Ring" || n == "HSI" || n == "FC" || n == "BS");

            int radius = circle ? Mathf.RoundToInt(w * 0.5f)
                                : Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(w, h) * 0.12f), 2, 8);
            if (!clear && img.sprite == null) UIShapes.Round(img, radius);
            // 완전 투명 이미지는 원래 Outline 이 그려지지 않았다 — 의도된 마커이므로 링으로 보이게 한다
            UIShapes.AddBorder(rt, radius, new Color(c.r, c.g, c.b, Mathf.Clamp(c.a, 0.35f, 0.9f)));
        }
    }

    // ── 2. 프레임 패널 질감 ──────────────────────────────────────────────
    static void SkinPanels(Canvas canvas)
    {
        foreach (var name in new[] { "FrameL", "FrameR", "CanopyTop", "GlareshieldL", "GlareshieldR" })
        {
            var t = canvas.transform.Find(name) as RectTransform;
            if (t == null || t.Find("Sheen") != null) continue;

            var g = UIBackdropFit.MakeGradient(t, "Sheen", false, Green, 0f, 0.012f);
            g.anchorMin = Vector2.zero; g.anchorMax = Vector2.one;
            g.offsetMin = g.offsetMax = Vector2.zero;

            if (name.StartsWith("Glareshield"))
            {
                var k = new GameObject("Keyline", typeof(RectTransform)).GetComponent<RectTransform>();
                k.SetParent(t, false);
                k.anchorMin = new Vector2(0, 1); k.anchorMax = new Vector2(1, 1);
                k.pivot = new Vector2(0.5f, 1);
                k.offsetMin = new Vector2(0, -1); k.offsetMax = Vector2.zero;
                var ki = k.gameObject.AddComponent<Image>();
                ki.color = new Color(0f, 0.98f, 0.42f, 0.34f); ki.raycastTarget = false;
            }
        }
    }

    // ── 3. 섹션 카드 ─────────────────────────────────────────────────────
    static void AddCards(Canvas canvas)
    {
        AddCard(canvas, "GlareshieldL", "ENGINE", "EngCard", new Vector2(132f, 252f), -110f);
        AddCard(canvas, "GlareshieldR", "WEAPON", "WpnCard", new Vector2(146f, 292f), -134f);
    }

    static void AddCard(Canvas canvas, string panelName, string headerText, string cardName, Vector2 size, float dy)
    {
        var panel = canvas.transform.Find(panelName) as RectTransform;
        if (panel == null || panel.Find(cardName) != null) return;

        Text header = null;
        foreach (var tx in panel.GetComponentsInChildren<Text>(true))
            if (tx.text == headerText) { header = tx; break; }
        if (header == null) return;

        var hp = ((RectTransform)header.transform).anchoredPosition;
        var rt = new GameObject(cardName, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(panel, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(hp.x, hp.y + dy);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        UIShapes.Round(img, 6);
        img.color = new Color(0.01f, 0.05f, 0.03f, 0.55f);
        UIShapes.AddBorder(rt, 6, new Color(0f, 0.98f, 0.42f, 0.28f));
        rt.SetSiblingIndex(0);                                  // 섹션 텍스트 뒤로
    }

    // ── 4. 색 보정 ───────────────────────────────────────────────────────
    static void RemapColors(Canvas canvas)
    {
        foreach (var tx in canvas.GetComponentsInChildren<Text>(true)) tx.color = Remap(tx.color);
        foreach (var im in canvas.GetComponentsInChildren<Image>(true))
        {
            if (im.sprite != null) continue;                    // 링/둥근 패널은 건드리지 않는다
            im.color = Remap(im.color);
        }
    }

    static Color Remap(Color c)
    {
        if (Near(c, OldHGD)) return NewHGD;
        if (Near(c, OldHGX)) return NewHGX;
        return c;
    }

    static bool Near(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f &&
           Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;
}
