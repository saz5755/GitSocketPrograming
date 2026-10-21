using UnityEngine;
using UnityEngine.UI;

/// <summary>세로 그라데이션 텍스트 (legacy Text / Image 정점 색 보간). 3단: 상단 → 중간 → 하단.</summary>
[RequireComponent(typeof(Graphic))]
public class UIGradientText : BaseMeshEffect
{
    public Color top = Color.white, mid = new Color(0.85f, 0.92f, 1f), bottom = new Color(0.45f, 0.62f, 0.95f);

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        var v = new UIVertex();
        float yMin = float.MaxValue, yMax = float.MinValue;
        for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); yMin = Mathf.Min(yMin, v.position.y); yMax = Mathf.Max(yMax, v.position.y); }
        float h = Mathf.Max(0.001f, yMax - yMin);
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float t = (v.position.y - yMin) / h;              // 0 하단 ~ 1 상단
            Color c = t > 0.5f ? Color.Lerp(mid, top, (t - 0.5f) * 2f) : Color.Lerp(bottom, mid, t * 2f);
            v.color = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(v.color.a * c.a));
            vh.SetUIVertex(v, i);
        }
    }
}

/// <summary>시안 스타일 타이포 유틸: 그라데이션/글로우/자간(공백 삽입)/강조선.</summary>
public static class UIType
{
    public static readonly Color Ice   = new Color(0.97f, 0.99f, 1f);
    public static readonly Color Steel = new Color(0.78f, 0.87f, 1f);
    public static readonly Color Blue  = new Color(0.40f, 0.60f, 1f);
    public static readonly Color Cyan  = new Color(0.28f, 0.84f, 0.95f);

    public static UIGradientText Gradient(Graphic g, Color top, Color mid, Color bottom)
    {
        var e = g.GetComponent<UIGradientText>();
        if (e == null) e = g.gameObject.AddComponent<UIGradientText>();
        e.top = top; e.mid = mid; e.bottom = bottom;
        g.SetVerticesDirty();
        return e;
    }

    /// <summary>은은한 시안 후광 + 하단 그림자</summary>
    public static void Glow(Graphic g, Color glow, float spread = 3f, float alpha = 0.22f)
    {
        var s1 = g.gameObject.AddComponent<Shadow>();
        s1.effectColor = new Color(glow.r, glow.g, glow.b, alpha); s1.effectDistance = new Vector2(0f, spread); s1.useGraphicAlpha = false;
        var s2 = g.gameObject.AddComponent<Shadow>();
        s2.effectColor = new Color(0f, 0.02f, 0.08f, 0.55f); s2.effectDistance = new Vector2(0f, -spread * 1.4f); s2.useGraphicAlpha = false;
    }

    /// <summary>글자 사이에 공백을 넣어 넓은 자간 (공백 문자는 단어 간격으로 확장)</summary>
    public static string Track(string s, int gap = 1)
    {
        var sb = new System.Text.StringBuilder();
        string pad = new string(' ', gap);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ') { sb.Append(pad).Append(pad); continue; }
            sb.Append(s[i]);
            if (i + 1 < s.Length && s[i + 1] != ' ') sb.Append(pad);
        }
        return sb.ToString();
    }

    /// <summary>시안 → 투명 강조선 (가로)</summary>
    public static RectTransform AccentLine(RectTransform parent, Vector2 pos, float width, float height, Color c, float aStart = 0.95f, float aEnd = 0f, bool leftPivot = true)
    {
        var rt = UIBackdropFit.MakeGradient(parent, "Accent", true, c, aStart, aEnd);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(leftPivot ? 0f : 1f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, height);
        return rt;
    }

    /// <summary>스윕(가속 빛줄기) — 로고 아래를 가로지르는 가산 광선</summary>
    public static RawImage Swoosh(RectTransform parent, Vector2 pos, Vector2 size, Color c)
    {
        var tex = UIGlowKit.Streak;
        if (tex == null) return null;
        var r = UIGlowKit.MakeRaw(parent, "Swoosh", tex, c, true);
        var rt = r.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        rt.localRotation = Quaternion.Euler(0f, 0f, 3f);
        return r;
    }
}
