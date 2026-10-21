using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 불꽃/글로우 연출용 절차적 텍스처와 가산(additive) 머티리얼.
/// 외부 에셋 없이 로그인/출격 연출에서 공통으로 사용한다.
/// </summary>
public static class UIGlowKit
{
    static Material _add;
    static Texture2D _radial, _streak, _scan;

    /// <summary>가산 합성 머티리얼. 셰이더 로드 실패 시 null (기본 알파 블렌딩으로 대체됨).</summary>
    public static Material Additive
    {
        get
        {
            if (_add != null) return _add;
            var sh = Resources.Load<Shader>("UI/KF21UIAdditive");
            if (sh != null) _add = new Material(sh) { hideFlags = HideFlags.DontSave };
            return _add;
        }
    }

    /// <summary>중심에서 바깥으로 부드럽게 사라지는 원형 글로우 (흰색 + 알파).</summary>
    public static Texture2D Radial
    {
        get
        {
            if (_radial != null) return _radial;
            const int N = 128;
            var t = NewTex(N, N);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                float a = Mathf.Pow(1f - r, 2.4f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return _radial = t;
        }
    }

    /// <summary>
    /// 오른쪽으로 뻗는 불꽃 줄기: 왼쪽(분사구)은 백열 → 주황 → 붉은색으로 식으며 사라진다.
    /// 세로는 가우시안, 길이 방향으로 점점 가늘어진다.
    /// </summary>
    public static Texture2D Streak
    {
        get
        {
            if (_streak != null) return _streak;
            const int W = 256, H = 64;
            var t = NewTex(W, H);
            var px = new Color32[W * H];
            var hot  = new Color(1.00f, 0.96f, 0.80f);
            var mid  = new Color(1.00f, 0.55f, 0.16f);
            var cold = new Color(0.85f, 0.18f, 0.06f);
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                Color c = u < 0.35f ? Color.Lerp(hot, mid, u / 0.35f) : Color.Lerp(mid, cold, (u - 0.35f) / 0.65f);
                float len = Mathf.Pow(1f - u, 1.5f) * Mathf.SmoothStep(0f, 1f, u / 0.14f);   // 분사구는 부드럽게 시작
                float width = Mathf.Lerp(1f, 0.35f, u);
                for (int y = 0; y < H; y++)
                {
                    float v = ((y + 0.5f) / H * 2f - 1f) / width;
                    float g = Mathf.Exp(-v * v * 3.2f);
                    px[y * W + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)Mathf.RoundToInt(g * len * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return _streak = t;
        }
    }

    /// <summary>세로 스캔 라인: 좌우로 사라지는 밝은 띠 (시안색).</summary>
    public static Texture2D Scan
    {
        get
        {
            if (_scan != null) return _scan;
            const int W = 64, H = 4;
            var t = NewTex(W, H);
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f;
                float a = Mathf.Exp(-u * u * 9f);
                for (int y = 0; y < H; y++) t.SetPixel(x, y, new Color(0.7f, 0.95f, 1f, a));
            }
            t.Apply();
            return _scan = t;
        }
    }

    static Texture2D NewTex(int w, int h)
    {
        return new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
    }

    /// <summary>RawImage 한 장 생성 (레이캐스트 차단 없음). additive=true 면 가산 합성.</summary>
    public static RawImage MakeRaw(Transform parent, string name, Texture tex, Color color, bool additive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var raw = go.AddComponent<RawImage>();
        raw.texture = tex;
        raw.color = color;
        raw.raycastTarget = false;
        if (additive && Additive != null) raw.material = Additive;
        return raw;
    }
}
