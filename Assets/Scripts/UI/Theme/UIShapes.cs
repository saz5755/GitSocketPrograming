using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코드로 만든 둥근 모서리 9-slice 스프라이트 (채움 / 테두리 링).
/// 외부 에셋 없이 시안의 라운드 패널·버튼 느낌을 내기 위한 공통 도구.
/// </summary>
public static class UIShapes
{
    static readonly Dictionary<int, Sprite> _fill = new Dictionary<int, Sprite>();
    static readonly Dictionary<int, Sprite> _ring = new Dictionary<int, Sprite>();

    public static Sprite Fill(int radius)
    {
        if (!_fill.TryGetValue(radius, out var s) || s == null)
            _fill[radius] = s = Build(radius, false);
        return s;
    }

    public static Sprite Ring(int radius)
    {
        if (!_ring.TryGetValue(radius, out var s) || s == null)
            _ring[radius] = s = Build(radius, true);
        return s;
    }

    /// <summary>Image 를 둥근 9-slice 채움으로 바꾼다.</summary>
    public static Image Round(Image img, int radius)
    {
        img.sprite = Fill(radius);
        img.type = Image.Type.Sliced;
        return img;
    }

    /// <summary>부모를 꽉 채우는 둥근 테두리(1.5px) Image 를 자식으로 추가한다.</summary>
    public static Image AddBorder(Transform parent, int radius, Color color)
    {
        var go = new GameObject("Border");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = Ring(radius);
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// 패널 스킨(그림자 → 둥근 본체 → 테두리)을 패널의 첫 자식들로 추가한다.
    /// 패널 자체에는 Image 를 붙이지 말 것 (부모 그래픽은 자식 위가 아니라 아래에 그려지므로 그림자가 본체를 덮는다).
    /// </summary>
    public static void AddPanelSkin(RectTransform panel, int radius, Color fill, Color border, bool shadow = true)
    {
        var skin = new List<Transform>();
        if (shadow)
        {
            var sh = NewStretch("Shadow", panel);
            sh.offsetMin = new Vector2(-28f, -40f); sh.offsetMax = new Vector2(28f, 16f);
            var si = sh.gameObject.AddComponent<Image>();
            Round(si, radius + 10);
            si.color = new Color(0f, 0f, 0f, 0.22f); si.raycastTarget = false;
            skin.Add(sh);
        }
        var fl = NewStretch("Fill", panel);
        var fi = fl.gameObject.AddComponent<Image>();
        Round(fi, radius);
        fi.color = fill;
        skin.Add(fl);
        skin.Add(AddBorder(panel, radius, border).transform);
        for (int i = 0; i < skin.Count; i++) skin[i].SetSiblingIndex(i);
    }

    static RectTransform NewStretch(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Sprite Build(int radius, bool ring)
    {
        int size = radius * 2 + 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float half = size * 0.5f;
        float inner = half - radius;          // 모서리 원의 중심까지의 거리
        const float thick = 1.5f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float qx = Mathf.Abs(x + 0.5f - half) - inner;
            float qy = Mathf.Abs(y + 0.5f - half) - inner;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            float dist = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
            float aOuter = Mathf.Clamp01(0.5f - dist);
            float a = aOuter;
            if (ring) a = aOuter - Mathf.Clamp01(0.5f - (dist + thick));
            px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
    }
}
