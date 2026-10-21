using UnityEngine;

/// <summary>
/// 로그인 인증 중 연출: 배경 사진 속 KF-21 후방에서 애프터버너 열기가 서서히 점화된다.
/// 배경 사진 RawImage(원본 비율 유지) 에 붙이면 자식으로 글로우 레이어를 만든다 — 좌표는 사진 기준 정규화 값이라
/// 해상도/비율이 달라도 같은 위치에 그려진다.
/// </summary>
public class LoginJetFX : MonoBehaviour
{
    // 사진 내 위치 (login_bg.png 1509x1143 기준 px)
    const float ImgW = 1509f, ImgH = 1143f;
    static readonly Vector2 TailPx = new Vector2(1235f, 628f);     // 후방 열기가 번지는 지점

    const float IgniteAt   = 0.2f;   // 점화 시작
    const float IgniteTime = 2.0f;   // 점화 완료까지

    RectTransform _rt;
    RectTransform _glowRoot, _outerRT, _coreRT, _streakRT;
    UnityEngine.UI.RawImage _outer, _core, _streak;
    UnityEngine.UI.RawImage[] _embers;
    float[] _emberT, _emberSeed;

    bool  _built;
    bool  _playing;
    float _t;
    float _fade = 0f, _fadeTarget = 0f;
    public float Elapsed => _t;
    public bool Playing => _playing;

    bool Build()
    {
        if (_built) return true;
        _rt = (RectTransform)transform;

        var gr = new GameObject("TailGlow");
        gr.transform.SetParent(transform, false);
        _glowRoot = gr.AddComponent<RectTransform>();
        Vector2 a = new Vector2(TailPx.x / ImgW, 1f - TailPx.y / ImgH);
        _glowRoot.anchorMin = _glowRoot.anchorMax = a;
        _glowRoot.sizeDelta = Vector2.zero;

        _outer  = UIGlowKit.MakeRaw(_glowRoot, "Outer",  UIGlowKit.Radial, new Color(1f, 0.42f, 0.10f, 0f), true);
        _outerRT = _outer.rectTransform;
        _streak = UIGlowKit.MakeRaw(_glowRoot, "Streak", UIGlowKit.Streak, new Color(1f, 1f, 1f, 0f), true);
        _streakRT = _streak.rectTransform;
        _streakRT.pivot = new Vector2(0f, 0.5f);
        _streakRT.localRotation = Quaternion.Euler(0f, 0f, 7f);
        _core   = UIGlowKit.MakeRaw(_glowRoot, "Core",   UIGlowKit.Radial, new Color(1f, 0.92f, 0.70f, 0f), true);
        _coreRT = _core.rectTransform;

        // 불씨
        const int N = 18;
        _embers = new UnityEngine.UI.RawImage[N]; _emberT = new float[N]; _emberSeed = new float[N];
        for (int i = 0; i < N; i++)
        {
            var e = UIGlowKit.MakeRaw(_glowRoot, "Ember" + i, UIGlowKit.Radial, new Color(1f, 0.6f, 0.2f, 0f), true);
            e.rectTransform.sizeDelta = new Vector2(8f, 8f);
            _embers[i] = e;
            _emberSeed[i] = Random.value * 100f;
            _emberT[i] = Random.value;
        }

        _glowRoot.gameObject.SetActive(false);
        _built = true;
        return true;
    }

    public void Play()
    {
        if (!Build()) return;
        _playing = true; _t = 0f; _fadeTarget = 1f; _fade = 1f;
    }

    public void Stop()
    {
        _playing = false; _fadeTarget = 0f;
    }

    void Update()
    {
        if (!_built) return;
        float dt = Time.unscaledDeltaTime;
        if (_playing) _t += dt;
        _fade = Mathf.MoveTowards(_fade, _fadeTarget, dt * (_fadeTarget > _fade ? 4f : 1.8f));
        bool on = _fade > 0.001f || _playing;
        if (_glowRoot.gameObject.activeSelf != on) _glowRoot.gameObject.SetActive(on);
        if (!on) return;
        ApplyLayout();
        ApplyState(_fade);
    }

    // 사진 크기 기준 요소 크기 갱신
    void ApplyLayout()
    {
        float sh = _rt.rect.height;
        _outerRT.sizeDelta  = new Vector2(sh * 0.56f, sh * 0.56f);
        _coreRT.sizeDelta   = new Vector2(sh * 0.15f, sh * 0.15f);
        _streakRT.sizeDelta = new Vector2(sh * 0.70f, sh * 0.22f);
    }

    void ApplyState(float vis)
    {
        float t = _t;
        // 점화: 서서히 달아오름 (smoothstep) + 불규칙한 흔들림
        float ig = Mathf.Clamp01((t - IgniteAt) / IgniteTime);
        float ign = ig * ig * (3f - 2f * ig);
        float flick = 0.88f + 0.12f * Mathf.PerlinNoise(t * 9f, 0.37f) + 0.05f * Mathf.Sin(t * 31f);
        float inten = ign * flick * vis;

        _outer.color  = WithA(new Color(1f, 0.40f + 0.12f * ign, 0.10f), 0.55f * inten);
        _core.color   = WithA(new Color(1f, 0.90f, 0.66f), 0.85f * inten * ign);
        _streak.color = WithA(Color.white, 0.95f * inten * ign);
        float len = 0.35f + 0.65f * ign;
        _streakRT.localScale = new Vector3(len * (0.94f + 0.06f * flick), 0.55f + 0.45f * ign, 1f);
        float pulse = 0.92f + 0.08f * Mathf.Sin(t * 14f);
        _outerRT.localScale = Vector3.one * (0.55f + 0.45f * ign) * pulse;
        _coreRT.localScale  = Vector3.one * (0.4f + 0.6f * ign);

        // 불씨: 점화 후 오른쪽 위로 흩날림
        float sh = _rt.rect.height;
        for (int i = 0; i < _embers.Length; i++)
        {
            if (ign < 0.15f) { _embers[i].color = WithA(_embers[i].color, 0f); continue; }
            _emberT[i] += Time.unscaledDeltaTime * (0.45f + 0.35f * Mathf.PerlinNoise(_emberSeed[i], 0.1f));
            if (_emberT[i] > 1f) { _emberT[i] -= 1f; _emberSeed[i] = Random.value * 100f; }
            float u = _emberT[i];
            float sx = _emberSeed[i];
            float dx = (0.05f + u * (0.34f + 0.2f * Mathf.PerlinNoise(sx, 0.7f))) * sh;
            float dy = ((Mathf.PerlinNoise(sx, 1.3f) - 0.5f) * 0.10f + u * 0.07f) * sh
                       + Mathf.Sin(u * 9f + sx) * 0.006f * sh;
            _embers[i].rectTransform.anchoredPosition = new Vector2(dx, dy);
            float a = Mathf.Sin(u * Mathf.PI) * ign * vis * 0.9f;
            _embers[i].color = new Color(1f, Mathf.Lerp(0.78f, 0.28f, u), Mathf.Lerp(0.35f, 0.08f, u), a);
        }
    }

    static Color WithA(Color c, float a) { c.a = a; return c; }
}
