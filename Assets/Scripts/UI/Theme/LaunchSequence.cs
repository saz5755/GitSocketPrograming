using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 출격 연출: 로비 UI 가 사라지고 홀로그램 KF-21 이 기수를 돌려 터널 정면을 향한다.
/// 와이어가 솔리드 기체로 굳은 뒤 애프터버너가 점화되고, 게이트 링을 차례로 통과하며 가속한다.
/// 섬광 후 다음 씬으로 이동. 로비의 HoloStage 를 그대로 이어받아 끊김 없이 진행한다.
/// 에셋이 없으면 연출을 건너뛰고 바로 씬을 로드한다.
/// </summary>
public class LaunchSequence : MonoBehaviour
{
    // ── 타임라인(초) ──────────────────────────────────────────────────────
    const float TTurnEnd  = 1.50f;   // 기수 정렬/카메라 후방 이동 완료
    const float TSolid    = 1.55f;   // 홀로그램 → 솔리드
    const float TIgnBeg   = 1.80f;   // 점화
    const float TIgnEnd   = 2.60f;
    const float TLaunch   = 2.50f;   // 문을 향해 진입 시작
    const float TEnter    = 5.50f;   // 문에 도달 (진입 3초)
    const float TFlash    = 6.10f;   // 화이트아웃 시작
    const float TEnd      = 6.90f;   // 씬 전환

    // ── 상태 ──────────────────────────────────────────────────────────────
    string _scene;
    Action _onBeforeLoad;
    float _t;
    public float DebugTime = -1f;           // 0 이상이면 이 시간으로 고정 (프리뷰용)
    bool _loading, _done;

    Transform _canvas;
    RectTransform _overlay;
    CanvasGroup _overlayCG;
    HoloStage _stage;
    bool _ownStage;
    Image _flash;
    Image _progress;
    Text _caption;
    AsyncOperation _op;
    CanvasGroup[] _fadeGroups;
    Font _font;

    // 시작 시점 스냅샷
    Vector3 _jetP0; float _jetYaw0;
    Vector3 _camP0; Quaternion _camR0; float _fov0;

    // ── 진입점 ────────────────────────────────────────────────────────────
    public static LaunchSequence Play(Transform canvas, string sceneName, Action onBeforeLoad = null)
    {
        var go = new GameObject("[LaunchSequence]");
        go.transform.SetParent(canvas, false);
        var ls = go.AddComponent<LaunchSequence>();
        ls._canvas = canvas; ls._scene = sceneName; ls._onBeforeLoad = onBeforeLoad;
        if (!ls.Build())
        {
            // 에셋 누락 — 연출 없이 즉시 전환
            onBeforeLoad?.Invoke();
            SceneManager.LoadScene(sceneName);
            Destroy(go);
            return null;
        }
        return ls;
    }

    // ── 구성 ──────────────────────────────────────────────────────────────
    bool Build()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _stage = FindAnyObjectByType<HoloStage>();
        if (_stage == null)
        {
            // 스테이지가 없으면(로비 외) 오버레이 안에 새로 만든다
            BuildOverlay();
            _stage = HoloStage.Create(_overlay);
            if (_stage == null) return false;
            _stage.transform.SetAsFirstSibling();
            _ownStage = true;
        }
        else BuildOverlay();

        _stage.Idle = false;
        _stage.HideScan();
        _jetP0 = _stage.Jet.position - HoloStage.Origin;
        _jetYaw0 = _stage.Jet.eulerAngles.y;
        _camP0 = _stage.Cam.transform.position - HoloStage.Origin;
        _camR0 = _stage.Cam.transform.rotation;
        _fov0 = _stage.Cam.fieldOfView;

        // 로비 UI 페이드 대상 (스테이지/오버레이 제외)
        var list = new System.Collections.Generic.List<CanvasGroup>();
        foreach (Transform c in _canvas)
        {
            if (c == _overlay || c.GetComponent<LaunchSequence>() != null || c.GetComponentInChildren<HoloStage>(true) != null) continue;
            var cg = c.GetComponent<CanvasGroup>();
            if (cg == null) cg = c.gameObject.AddComponent<CanvasGroup>();
            list.Add(cg);
        }
        _fadeGroups = list.ToArray();

        // 다음 씬을 미리 로드 (활성화는 연출 종료 시)
        Application.backgroundLoadingPriority = ThreadPriority.Low;
        _op = SceneManager.LoadSceneAsync(_scene);
        if (_op != null) _op.allowSceneActivation = false;
        return true;
    }

    void BuildOverlay()
    {
        var th = UITheme.Instance;
        var go = new GameObject("LaunchOverlay");
        go.transform.SetParent(_canvas, false);
        _overlay = go.AddComponent<RectTransform>();
        _overlay.anchorMin = Vector2.zero; _overlay.anchorMax = Vector2.one;
        _overlay.offsetMin = _overlay.offsetMax = Vector2.zero;
        _overlayCG = go.AddComponent<CanvasGroup>();
        _overlayCG.blocksRaycasts = true;
        go.transform.SetAsLastSibling();

        // 캡션 + 진행 라인
        var cap = new GameObject("Caption");
        cap.transform.SetParent(_overlay, false);
        var crt = cap.AddComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0f, 0f);
        crt.pivot = new Vector2(0f, 0f);
        crt.anchoredPosition = new Vector2(110f, 64f);
        crt.sizeDelta = new Vector2(700f, 22f);
        _caption = cap.AddComponent<Text>();
        _caption.font = _font; _caption.fontSize = th.textS; _caption.color = new Color(th.accentCyan.r, th.accentCyan.g, th.accentCyan.b, 0f);
        _caption.alignment = TextAnchor.MiddleLeft; _caption.raycastTarget = false;
        _caption.text = "S O R T I E   ·   K F - 2 1   B O R A M A E";

        var line = new GameObject("ProgressLine");
        line.transform.SetParent(_overlay, false);
        var lrt = line.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
        lrt.pivot = new Vector2(0f, 0.5f);
        lrt.anchoredPosition = new Vector2(110f, 52f);
        lrt.sizeDelta = new Vector2(0f, 2f);
        _progress = line.AddComponent<Image>();
        _progress.color = th.accentCyan; _progress.raycastTarget = false;

        var hint = new GameObject("SkipHint");
        hint.transform.SetParent(_overlay, false);
        var hrt = hint.AddComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(1f, 0f);
        hrt.pivot = new Vector2(1f, 0f);
        hrt.anchoredPosition = new Vector2(-110f, 62f);
        hrt.sizeDelta = new Vector2(400f, 20f);
        var ht = hint.AddComponent<Text>();
        ht.font = _font; ht.fontSize = th.textXS; ht.color = new Color(1f, 1f, 1f, 0.4f);
        ht.alignment = TextAnchor.MiddleRight; ht.raycastTarget = false;
        ht.text = "SPACE / 클릭  건너뛰기";

        // 섬광
        var fgo = new GameObject("Flash");
        fgo.transform.SetParent(_overlay, false);
        var frt = fgo.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
        _flash = fgo.AddComponent<Image>();
        _flash.color = new Color(0.8f, 0.95f, 1f, 0f); _flash.raycastTarget = false;
    }

    // ── 루프 ──────────────────────────────────────────────────────────────
    void Update()
    {
        if (_done) return;
        if (DebugTime >= 0f) _t = DebugTime;
        else _t += Time.unscaledDeltaTime;

        // 건너뛰기
        if (DebugTime < 0f && _t < TFlash - 0.25f && _t > 0.6f &&
            (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
             Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0)))
            _t = TFlash - 0.2f;

        Evaluate(_t);

        if (_t >= TEnd && !_loading) StartCoroutine(Finish());
    }

    void Evaluate(float t)
    {
        // UI 페이드아웃
        float ui = 1f - Smooth(t / 0.45f);
        foreach (var g in _fadeGroups) if (g != null) { g.alpha = ui; g.blocksRaycasts = false; }
        _progress.rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(t / TEnd) * 420f, 2f);
        var cc = _caption.color; cc.a = 0.9f * Smooth((t - 0.3f) / 0.4f); _caption.color = cc;

        // ── 기체 포즈 ─────────────────────────────────────────────────────
        float turn = Smooth(t / TTurnEnd);
        Vector3 hover = new Vector3(0f, HoloStage.GateGroundY, 22f);
        Vector3 pos = Vector3.Lerp(_jetP0, hover, turn);
        pos.y += Mathf.Sin(t * 1.7f) * 0.08f * (1f - turn);
        float yaw = Mathf.LerpAngle(_jetYaw0, 0f, turn);
        float pitch = 0f, roll = (1f - turn) * Mathf.Sin(turn * 3.14f) * -10f;
        float speed = 0f;

        // 점화 진동 + 앞으로 살짝 밀림: 출발(TLaunch) 이후에도 끊기지 않고 이어지도록 항상 적용 (진동만 서서히 소멸)
        float shakeT = Smooth((t - TIgnBeg) / (TIgnEnd - TIgnBeg));
        const float PushZ = 0.6f;
        if (t >= TIgnBeg)
        {
            float amp = shakeT * (1f - Smooth((t - TLaunch) / 0.4f));
            pos += new Vector3(Mathf.Sin(t * 53f), Mathf.Sin(t * 71f), 0f) * 0.02f * amp;
            pos.z += shakeT * shakeT * PushZ;
        }

        float u = 0f;   // 진입 진행도 0~1 (문까지 3초)
        if (t >= TLaunch)
        {
            float d = t - TLaunch, span = TEnter - TLaunch;
            const float gap = 78f;
            u = Mathf.Clamp01(d / span);
            float v0 = gap * 2.1f / span;                        // 문 도달 시 속도
            if (d <= span)
            {
                pos.z += gap * Mathf.Pow(u, 2.1f);
                speed = v0 * Mathf.Pow(u, 1.1f);
                // 지면 활주 → 막바지에 기수를 들며 이륙
                float rot = Smooth((u - 0.72f) / 0.28f);
                pitch = -rot * 9f;
                pos.y += rot * rot * 1.2f;
            }
            else
            {
                float e = d - span;
                pos.z += gap + v0 * e + 220f * e * e;
                speed = v0 + 440f * e;
                pitch = -9f;
                pos.y += 1.2f + e * 6f;
            }
        }
        _stage.MoveJet(pos, Quaternion.Euler(pitch, yaw, roll));

        // ── 재질: 홀로그램 → 솔리드 ──────────────────────────────────────
        float holoIn = Smooth((t - 0.1f) / 1.2f);
        _stage.SetHoloFill(Mathf.Lerp(0.02f, 0.10f, holoIn));
        _stage.SetHoloIntensity(Mathf.Lerp(0.30f, 0.60f, holoIn));
        _stage.ShowPad(t < 1.2f);
        if (t >= TSolid && !_stage.Solid)
        {
            _stage.SetSolid(true);
        }
        if (_stage.Solid) _stage.SetSolidBlend(Smooth((t - TSolid) / 0.85f));   // 와이어가 사라지며 조명이 서서히 켜진다
        float solidFlash = t >= TSolid ? Mathf.Clamp01(1f - (t - TSolid) / 0.22f) * 0.06f : 0f;

        // ── 디지털 입구: 출격 준비 중 서서히 열린다 ──
        _stage.SetPortalOpen(Smooth((t - 1.2f) / 2.6f));
        _stage.SetPortalEnergy(Smooth((t - 3.4f) / 2.0f));

        // ── 화염 ─────────────────────────────────────────────────────────
        float ig = Smooth((t - TIgnBeg) / (TIgnEnd - TIgnBeg));
        float boost = t >= TLaunch ? Mathf.Lerp(1f, 1.5f, Smooth((t - TLaunch) / 0.5f)) : 1f;
        _stage.SetFlame(ig, boost);
        _stage.SetGrounded(t > 0.4f, ig * (1f - Smooth(u * 1.4f)));

        // ── 카메라: 로비 구도 → 기체 후방 추적 → 문에 빨려드는 광각 ───────────────
        Vector3 chase = new Vector3(1.6f, 2.9f, hover.z - 27f);
        float ct = Smooth((t - 0.1f) / (TTurnEnd - 0.1f));
        Vector3 cp = Vector3.Lerp(_camP0, chase, ct);
        float fov = Mathf.Lerp(_fov0, 34f, ct);
        if (t >= TLaunch)
        {
            // 기체보다 덜 따라가 거리가 벌어지고, 시선은 문 쪽으로 점점 모인다
            cp.z += Mathf.Max(0f, pos.z - hover.z - PushZ) * 0.78f;
            cp.y += (pos.y - hover.y) * 0.9f;
            cp.x = Mathf.Lerp(chase.x, 1.2f, Smooth(u));
            fov += 26f * u * u;
            cp += new Vector3(Mathf.Sin(t * 47f), Mathf.Sin(t * 61f), 0f) * 0.04f * u * u;   // 가속 진동
        }
        // 카메라는 게이트 입구 앞에서 멈춘다 (벽을 통과하며 화면이 검게 번쩍이는 것 방지).
        // 기체가 터널 속으로 빨려 들어가는 모습을 망원으로 지켜본다.
        cp.z = Mathf.Min(cp.z, HoloStage.GateZ - 9f);
        if (t > TEnter) fov = Mathf.Lerp(fov, 26f, Smooth((t - TEnter) / 0.8f));
        Quaternion cr = Quaternion.Slerp(_camR0, Quaternion.LookRotation((pos + new Vector3(0f, 0.3f, 0f)) - cp), Mathf.Max(ct, 0f));
        _stage.SetCamera(cp, cr, fov);
        _stage.StreakSpeed = 7f + speed * 0.9f;
        _stage.SetTunnelSpeed(speed);

        // ── 화이트아웃 ───────────────────────────────────────────────────
        float wo = Smooth((t - TFlash) / (TEnd - TFlash));
        // 솔리드 전환은 밝은 섬광, 터널 끝은 어두운 심해색으로 빨려 들어가듯 페이드
        _flash.color = wo > 0.001f ? new Color(0.01f, 0.03f, 0.07f, wo) : new Color(0.8f, 0.95f, 1f, solidFlash);
        _stage.FinishFrame();
    }

    // ── 종료 ──────────────────────────────────────────────────────────────
    IEnumerator Finish()
    {
        _loading = true;
        _onBeforeLoad?.Invoke();
        SceneCurtain.Raise();                               // 씬이 바뀌어도 남는 암막 (깜빡임 방지)
        if (_op != null)
        {
            while (_op.progress < 0.9f) yield return null;   // 사전 로드 완료 대기
            _done = true;
            _op.allowSceneActivation = true;
        }
        else
        {
            _done = true;
            SceneManager.LoadScene(_scene);
        }
    }

    void OnDestroy()
    {
        if (_overlay != null) Destroy(_overlay.gameObject);
        Application.backgroundLoadingPriority = ThreadPriority.Normal;
    }

    // ── 기체 머티리얼 보정 (로비 쇼케이스/출격 공용) ───────────────────────────
    static readonly System.Collections.Generic.List<UnityEngine.Object> _owned = new System.Collections.Generic.List<UnityEngine.Object>();
    public static Material[] TuneMaterials(Material[] src)
    {
        EnsureReflection();
        var res = new Material[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var m = new Material(src[i]) { hideFlags = HideFlags.DontSave };
            _owned.Add(m);
            string n = src[i].name;
            bool glass = n.StartsWith("cock");
            bool wheel = n.StartsWith("wheel");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", glass ? 0.9f : wheel ? 0.1f : 0.55f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", glass ? 0.92f : wheel ? 0.35f : 0.68f);
            if (glass && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.10f, 0.12f, 0.18f, 1f));
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 1f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 1f);
            res[i] = m;
        }
        return res;
    }

    static Cubemap _reflCube;

    // 푸른 네온 터널 느낌의 작은 큐브맵 (반사용)
    public static void EnsureReflection()
    {
        if (_reflCube != null)
        {
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = _reflCube;
            RenderSettings.reflectionIntensity = 1f;
            return;
        }
        const int S = 32;
        _reflCube = new Cubemap(S, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave };
        _owned.Add(_reflCube);
        Vector3 glowDir = Vector3.forward;                       // 터널 끝의 밝은 광원
        Vector3 warmDir = (Quaternion.Euler(18f, -20f, 0f) * Vector3.back).normalized;
        Color top = new Color(0.03f, 0.06f, 0.14f), hor = new Color(0.16f, 0.45f, 0.95f), gnd = new Color(0.02f, 0.04f, 0.09f);
        for (int f = 0; f < 6; f++)
        {
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    Vector3 d;
                    switch ((CubemapFace)f)
                    {
                        case CubemapFace.PositiveX: d = new Vector3(1, -v, -u); break;
                        case CubemapFace.NegativeX: d = new Vector3(-1, -v, u); break;
                        case CubemapFace.PositiveY: d = new Vector3(u, 1, v); break;
                        case CubemapFace.NegativeY: d = new Vector3(u, -1, -v); break;
                        case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1); break;
                        default: d = new Vector3(-u, -v, -1); break;
                    }
                    d.Normalize();
                    Color c = d.y >= 0f ? Color.Lerp(hor, top, Mathf.Pow(d.y, 0.5f)) : Color.Lerp(hor * 0.3f, gnd, Mathf.Clamp01(-d.y * 3f));
                    c += new Color(0.6f, 0.85f, 1f) * (Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, glowDir)), 10f) * 2.4f);
                    c += new Color(1f, 0.55f, 0.25f) * (Mathf.Pow(Mathf.Max(0f, Vector3.Dot(d, warmDir)), 18f) * 1.4f);
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            _reflCube.SetPixels(px, (CubemapFace)f);
        }
        _reflCube.Apply(true);
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = _reflCube;
        RenderSettings.reflectionIntensity = 1f;
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}


/// <summary>
/// 씬 전환용 지속 암막: 출격 연출의 마지막 암전 색 그대로 DontDestroyOnLoad 로 유지하다가,
/// 새 씬의 프레임이 안정된 뒤 천천히 걷는다. (씬 활성화 순간의 검은 깜빡임/첫 프레임 끊김을 가린다)
/// </summary>
public class SceneCurtain : MonoBehaviour
{
    static SceneCurtain _inst;
    CanvasGroup _cg;

    public static void Raise()
    {
        if (_inst != null) return;
        var go = new GameObject("[SceneCurtain]");
        DontDestroyOnLoad(go);
        var cv = go.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = short.MaxValue;
        var img = new GameObject("Dark").AddComponent<UnityEngine.UI.Image>();
        img.transform.SetParent(go.transform, false);
        img.color = new Color(0.01f, 0.03f, 0.07f, 1f); img.raycastTarget = false;
        var rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        _inst = go.AddComponent<SceneCurtain>();
        _inst._cg = go.AddComponent<CanvasGroup>(); _inst._cg.alpha = 1f; _inst._cg.blocksRaycasts = false;
        _inst.StartCoroutine(_inst.Run());
    }

    IEnumerator Run()
    {
        // 새 씬이 올라올 때까지 대기 → 프레임이 안정될 때까지 → 서서히 걷기
        string start = SceneManager.GetActiveScene().name;
        float t0 = Time.realtimeSinceStartup;
        while (SceneManager.GetActiveScene().name == start && Time.realtimeSinceStartup - t0 < 8f) yield return null;
        yield return UIStartup.WaitStable();
        float hold = 0.25f;
        while (hold > 0f) { hold -= Time.unscaledDeltaTime; yield return null; }
        float a = 1f;
        while (a > 0f)
        {
            a -= Mathf.Min(Time.unscaledDeltaTime, 0.05f) / 0.8f;
            _cg.alpha = a * a * (3f - 2f * a);
            yield return null;
        }
        _inst = null;
        Destroy(gameObject);
    }
}
