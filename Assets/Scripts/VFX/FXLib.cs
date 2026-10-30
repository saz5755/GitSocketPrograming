using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// VFX 공용 라이브러리 — 공유 머티리얼(셰이더당 1개 + 변형), 파티클 시스템 빌더, 점광원 빌보드.
/// 모든 셰이더는 Resources/VFX 에 있어 빌드에 포함되고, 텍스처 없이 절차적으로 형태를 만든다.
/// </summary>
public static class FXLib
{
    static readonly Dictionary<string, Material> s_mats = new();

    static Material Mat(string key, string shader, System.Action<Material> setup)
    {
        if (s_mats.TryGetValue(key, out var m) && m != null) return m;
        var sh = Shader.Find(shader);
        if (sh == null)
        {
            Debug.LogWarning($"[FXLib] 셰이더 없음: {shader}");
            sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        }
        m = new Material(sh) { name = key, hideFlags = HideFlags.DontSave };
        setup?.Invoke(m);
        s_mats[key] = m;
        return m;
    }

    // ── 머티리얼 ────────────────────────────────────────────────────────────
    public static Material Glow        => Mat("FX_Glow",       "Custom/FXGlow", m => { m.SetFloat("_Intensity", 3f); m.SetFloat("_Flicker", 0.3f); });
    public static Material GlowSoft    => Mat("FX_GlowSoft",   "Custom/FXGlow", m => { m.SetFloat("_Intensity", 2f); m.SetFloat("_Falloff", 2.6f); m.SetFloat("_Core", 0.2f); m.SetFloat("_Flicker", 0.15f); });
    public static Material Spark       => Mat("FX_Spark",      "Custom/FXGlow", m => { m.SetFloat("_Intensity", 6f); m.SetFloat("_Falloff", 1.2f); m.SetFloat("_Core", 1.2f); m.SetFloat("_Flicker", 0f); m.SetFloat("_SoftDist", 0.2f); });
    public static Material Tracer      => Mat("FX_Tracer",     "Custom/FXGlow", m => { m.SetFloat("_Intensity", 7f); m.SetFloat("_LineMode", 1f); m.SetFloat("_Core", 1f); m.SetFloat("_Flicker", 0f); m.SetFloat("_SoftDist", 0.1f); });
    public static Material Smoke       => Mat("FX_Smoke",      "Custom/FXSmoke", m => { m.SetColor("_Color", new Color(0.8f, 0.8f, 0.8f)); m.SetFloat("_Opacity", 0.85f); });
    public static Material SmokeDark   => Mat("FX_SmokeDark",  "Custom/FXSmoke", m => { m.SetColor("_Color", new Color(0.42f, 0.4f, 0.38f)); m.SetFloat("_Opacity", 0.9f); });
    public static Material SmokeFire   => Mat("FX_SmokeFire",  "Custom/FXSmoke", m =>
    {
        m.SetColor("_Color", new Color(0.48f, 0.45f, 0.42f));
        m.SetColor("_Ember", new Color(9f, 3.2f, 0.7f));
        m.SetFloat("_EmberAge", 0.22f);
        m.SetFloat("_Opacity", 1f);
    });
    public static Material MissileSmoke=> Mat("FX_MslSmoke",   "Custom/FXSmoke", m =>
    {
        m.SetColor("_Color", new Color(0.93f, 0.93f, 0.92f));
        m.SetFloat("_Opacity", 0.55f);
        m.SetFloat("_Erode", 0.85f);
        m.SetFloat("_NoiseScale", 1.8f);
        m.SetFloat("_Shade", 0.45f);
        m.SetFloat("_SoftDist", 4f);
    });
    // 에어쇼 연막 — 햇빛을 받아도 흰색은 희게 (일반 연기보다 밝고 자기 그림자 약하게), 컬러는 선명하게
    public static Material AirshowSmoke => Mat("FX_AirshowSmoke", "Custom/FXSmoke", m =>
    {
        m.SetColor("_Color", new Color(1.05f, 1.08f, 1.14f));
        m.SetFloat("_Opacity", 0.9f);
        m.SetFloat("_Erode", 0.8f);
        m.SetFloat("_NoiseScale", 2f);
        m.SetFloat("_Shade", 0.3f);
        m.SetFloat("_SoftDist", 3f);
    });
    public static Material Water       => Mat("FX_Water",      "Custom/FXSmoke", m =>
    {
        m.SetColor("_Color", new Color(0.95f, 0.97f, 1f));
        m.SetFloat("_Opacity", 0.9f);
        m.SetFloat("_Erode", 1.0f);
        m.SetFloat("_NoiseScale", 3.2f);
        m.SetFloat("_Shade", 0.35f);
    });
    public static Material Vapor       => Mat("FX_Vapor",      "Custom/FXSmoke", m =>
    {
        m.SetColor("_Color", new Color(0.97f, 0.98f, 1f));
        m.SetFloat("_Opacity", 0.35f);
        m.SetFloat("_Erode", 1.1f);
        m.SetFloat("_NoiseScale", 2.6f);
        m.SetFloat("_Shade", 0.25f);
        m.SetFloat("_SoftDist", 0.5f);
    });
    public static Material HeatHaze    => Mat("FX_HeatHaze",   "Custom/FXHeatHaze", null);
    public static Material Shockwave   => Mat("FX_Shockwave",  "Custom/FXHeatHaze", m => { m.SetFloat("_Strength", 0.06f); m.SetFloat("_Scale", 1.5f); m.SetFloat("_Speed", 0.5f); });
    public static Material FlareBase   => Mat("FX_Flare",      "Custom/FXFlare", null);

    // ── 파티클 빌더 ─────────────────────────────────────────────────────────
    /// <summary>꺼진 상태의 파티클 시스템을 만든다 (월드 시뮬레이션, 루프 없음 기본)</summary>
    public static ParticleSystem MakePS(string name, Transform parent, Material mat, int maxParticles,
                                        bool worldSpace = true, bool loop = false)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop            = loop;
        m.duration        = 1f;
        m.playOnAwake     = false;
        m.maxParticles    = maxParticles;
        m.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        m.scalingMode     = ParticleSystemScalingMode.Hierarchy;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial    = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows    = false;
        r.renderMode        = ParticleSystemRenderMode.Billboard;
        r.sortMode          = ParticleSystemSortMode.Distance;
        r.minParticleSize   = 0f;
        r.maxParticleSize   = 4f;      // 화면 높이 배수 — 가까이 지나갈 때 연기가 잘리지 않게
        if (mat != null && mat.shader != null && mat.shader.name == "Custom/FXSmoke")
            r.SetActiveVertexStreams(SmokeStreams);
        else
            r.SetActiveVertexStreams(GlowStreams);
        return ps;
    }

    static readonly List<ParticleSystemVertexStream> SmokeStreams = new()
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomXY,
        ParticleSystemVertexStream.AgePercent,
    };
    static readonly List<ParticleSystemVertexStream> GlowStreams = new()
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomXY,
    };

    public static void Burst(ParticleSystem ps, int count)
    {
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
    }

    public static ParticleSystem.MinMaxCurve Curve(params float[] kv)
    {
        var keys = new Keyframe[kv.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(kv[i * 2], kv[i * 2 + 1]);
        return new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
    }

    public static Gradient Grad(Color[] cols, float[] ct, float[] alphas, float[] at)
    {
        var g = new Gradient();
        var ck = new GradientColorKey[cols.Length];
        for (int i = 0; i < cols.Length; i++) ck[i] = new GradientColorKey(cols[i], ct[i]);
        var ak = new GradientAlphaKey[alphas.Length];
        for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], at[i]);
        g.SetKeys(ck, ak);
        return g;
    }

    public static void ColorOverLife(ParticleSystem ps, Gradient g)
    {
        var c = ps.colorOverLifetime; c.enabled = true; c.color = g;
    }

    public static void SizeOverLife(ParticleSystem ps, ParticleSystem.MinMaxCurve curve)
    {
        var s = ps.sizeOverLifetime; s.enabled = true; s.size = curve;
    }

    // ── 점광원 빌보드 ───────────────────────────────────────────────────────
    static Mesh s_quad;
    static Mesh Quad
    {
        get
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "FXFlareQuad", hideFlags = HideFlags.DontSave };
            s_quad.vertices  = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            s_quad.uv        = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            s_quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            s_quad.bounds    = new Bounds(Vector3.zero, Vector3.one * 40f);   // 빌보드 확대 범위까지 컬링 여유
            return s_quad;
        }
    }

    public class Flare
    {
        public MeshRenderer renderer;
        public Transform    transform;
        readonly MaterialPropertyBlock _mpb = new();
        static readonly int ID_Color = Shader.PropertyToID("_Color");
        static readonly int ID_Int   = Shader.PropertyToID("_Intensity");
        static readonly int ID_Size  = Shader.PropertyToID("_Size");
        static readonly int ID_Min   = Shader.PropertyToID("_MinAngle");
        static readonly int ID_Star  = Shader.PropertyToID("_Star");
        Color _c; float _size, _min, _star;

        public void Set(Color c, float intensity)
        {
            renderer.enabled = intensity > 0.001f;
            if (!renderer.enabled) return;
            _mpb.SetColor(ID_Color, c);
            _mpb.SetFloat(ID_Int, intensity);
            _mpb.SetFloat(ID_Size, _size);
            _mpb.SetFloat(ID_Min, _min);
            _mpb.SetFloat(ID_Star, _star);
            renderer.SetPropertyBlock(_mpb);
        }

        internal void Init(float size, float minAngle, float star) { _size = size; _min = minAngle; _star = star; }
    }

    /// <summary>parent 의 localPos 에 카메라를 향하는 점광원을 만든다</summary>
    public static Flare MakeFlare(string name, Transform parent, Vector3 localPos, float size, float minAngle = 0.0025f, float star = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<MeshFilter>().sharedMesh = Quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial     = FlareBase;
        mr.shadowCastingMode  = ShadowCastingMode.Off;
        mr.receiveShadows     = false;
        mr.lightProbeUsage    = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.enabled = false;
        var f = new Flare { renderer = mr, transform = go.transform };
        f.Init(size, minAngle, star);
        return f;
    }

    // ── 해수면 ──────────────────────────────────────────────────────────────
    static float s_seaY = float.NaN;
    public static float SeaY
    {
        get
        {
            if (float.IsNaN(s_seaY))
            {
                var ocean = Object.FindAnyObjectByType<OceanSurface>();
                s_seaY = ocean != null ? ocean.seaLevel : float.NegativeInfinity;
            }
            return s_seaY;
        }
    }
}
