using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조종사 G 내성 모델 + 시야 연출 (v4.1 F-2).
/// G 슈트·AGSM 기준 약 5.5G 까지는 무한 지속, 그 이상은 초과분만큼 "G 스트레스"가 쌓인다
/// (9G 약 6초, 7G 약 17초에 G-LOC). 스트레스에 따라 그레이아웃 → 터널 비전 → 블랙아웃(G-LOC),
/// G-LOC 중에는 스틱이 중립으로 풀리고(PlayerController.ControlsLocked) 몇 초 뒤 깨어난다.
/// 음의 G 는 레드아웃(붉은 시야)으로 표현한다.
/// GameModeManager 가 런타임에 생성한다.
/// </summary>
public class GLocSystem : MonoBehaviour
{
    public static GLocSystem Instance { get; private set; }

    const float Tolerance   = 5.5f;   // 무한 지속 가능 G
    const float RefG        = 9f;     // 이 G 에서 BuildTime 초면 G-LOC
    const float BuildTime   = 6f;
    const float RecoverRate = 0.22f;  // /s (저G 에서 1.5배)
    const float LocDuration  = 4.5f;  // 완전 의식 상실
    const float WakeDuration = 3f;    // 의식 회복 (앞 40% 동안은 여전히 조종 불능)

    /// <summary>양의 G 스트레스 0..1 (1 = G-LOC)</summary>
    public float Stress    { get; private set; }
    public float NegStress { get; private set; }
    public bool  Unconscious => _locTimer > 0f;

    float _locTimer, _wakeTimer;
    PlayerController _pc;

    CanvasGroup   _cg;
    Image         _grey, _red, _black, _tunnelImg;
    Image[]       _bars;
    RectTransform _tunnel;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_pc != null) _pc.ControlsLocked = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (_pc == null || !_pc.isLocalPlayer)
        {
            _pc = null;
            foreach (var p in PlayerController.All)
                if (p != null && p.isLocalPlayer) { _pc = p; break; }
        }

        var gm = GameModeManager.Instance;
        bool active = _pc != null && gm != null && gm.IsFlying && !gm.IsDead;
        if (!active)
        {
            if (_pc != null) _pc.ControlsLocked = false;
            Stress = NegStress = 0f; _locTimer = _wakeTimer = 0f;
            Render(0f);
            return;
        }

        float g = _pc.PilotG;

        // ── 양의 G ──
        if (_locTimer <= 0f)
        {
            if (g > Tolerance) Stress += (g - Tolerance) / (RefG - Tolerance) * dt / BuildTime;
            else               Stress -= RecoverRate * (g < 3f ? 1.5f : 1f) * dt;
            Stress = Mathf.Clamp01(Stress);
            if (Stress >= 1f) { _locTimer = LocDuration; _wakeTimer = 0f; }
        }
        else
        {
            _locTimer -= dt;
            if (_locTimer <= 0f) { _wakeTimer = WakeDuration; Stress = 0.6f; }
        }
        if (_wakeTimer > 0f) _wakeTimer -= dt;

        // ── 음의 G ──
        if (g < -1.5f) NegStress += (-1.5f - g) / 1.5f * dt / 4f;
        else           NegStress -= 0.3f * dt;
        NegStress = Mathf.Clamp01(NegStress);

        _pc.ControlsLocked = _locTimer > 0f || _wakeTimer > WakeDuration * 0.6f;

        float black = _locTimer > 0f ? 1f : Mathf.Clamp01(_wakeTimer / WakeDuration);
        Render(black);
    }

    void Render(float black)
    {
        float s = Stress;
        _grey.color  = new Color(0.32f, 0.32f, 0.32f, Mathf.SmoothStep(0f, 0.55f, Mathf.InverseLerp(0.3f, 0.75f, s)));
        _red.color   = new Color(0.55f, 0.02f, 0.02f, NegStress * 0.6f);
        _black.color = new Color(0f, 0f, 0f, black);

        // 터널: 스트레스 0.35 부터 시야가 바깥에서 안쪽으로 좁아진다
        float tunnel = Mathf.InverseLerp(0.35f, 0.95f, Mathf.Max(s, black));
        float scale  = Mathf.Lerp(2.4f, 0.55f, tunnel);
        _tunnel.localScale = new Vector3(scale, scale, 1f);
        float ta = tunnel > 0f ? 1f : 0f;
        _tunnelImg.color = new Color(0f, 0f, 0f, ta);
        foreach (var b in _bars) b.color = new Color(0f, 0f, 0f, ta);

        _cg.alpha = (s > 0.3f || black > 0f || NegStress > 0f) ? 1f : 0f;
    }

    // ── UI ──────────────────────────────────────────────────────────────────
    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;   // HUD 위, 일시정지 메뉴(50) 아래
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        _cg = gameObject.AddComponent<CanvasGroup>();
        _cg.blocksRaycasts = false; _cg.interactable = false;

        _grey = FullImage("Greyout", transform);

        _tunnel = new GameObject("Tunnel", typeof(RectTransform)).GetComponent<RectTransform>();
        _tunnel.SetParent(transform, false);
        _tunnel.anchorMin = _tunnel.anchorMax = new Vector2(0.5f, 0.5f);
        _tunnel.sizeDelta = new Vector2(1920f, 1920f);
        _tunnelImg = FullImage("Vignette", _tunnel);
        _tunnelImg.sprite = MakeVignette();
        // 비네트 사각형 바깥을 메우는 막대 4개
        _bars = new Image[4];
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        for (int i = 0; i < 4; i++)
        {
            var img = FullImage("Bar", _tunnel);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f) + dirs[i] * 0.5f;
            rt.pivot     = new Vector2(0.5f, 0.5f) - dirs[i] * 0.5f;
            rt.sizeDelta = new Vector2(8000f, 8000f);
            _bars[i] = img;
        }

        _red   = FullImage("Redout", transform);
        _black = FullImage("Blackout", transform);
        Render(0f);
    }

    static Image FullImage(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = Color.clear;
        return img;
    }

    static Sprite MakeVignette()
    {
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.92f, r));
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
    }
}
