using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 볼류메트릭 구름 레이어. 씬의 빈 GameObject 에 부착 (Inspector 에서 고도·양·조명 조절).
///  · [ExecuteAlways] — 플레이하지 않아도 씬/게임 뷰에 그려지고 Inspector 값이 즉시 반영된다
///  · 3D 노이즈(Perlin-Worley + Worley 3옥타브)는 Resources/Env/CloudNoise3D 에셋을 쓰고, 없으면 백그라운드 스레드에서 생성
///  · 카메라마다 전체 화면 삼각형 하나를 Graphics.RenderMesh 로 그려 Custom/CloudVolume 셰이더가 레이마칭한다
///  · 장면 깊이로 레이를 자르므로 Depth Texture 가 필요 (PC 렌더 파이프라인 에셋은 켜져 있음, 플레이 중엔 카메라에도 요청)
/// </summary>
[ExecuteAlways]
public class CloudLayer : MonoBehaviour
{
    [Header("고도 (월드 Y, m)")]
    public float cloudBase = 650f;
    public float cloudTop = 1150f;

    [Header("모양")]
    [Tooltip("하늘을 덮는 비율 (0 맑음 ~ 1 흐림)")]
    [Range(0f, 1f)] public float coverage = 0.45f;
    [Tooltip("구름 밀도 — 클수록 속이 꽉 찬 흰 구름")]
    [Range(0.005f, 0.15f)] public float extinction = 0.045f;
    [Tooltip("구름 덩어리 크기 (노이즈 한 타일의 월드 크기)")]
    public float shapeScale = 1400f;
    [Tooltip("구름 무리(군집) 간격")]
    public float clusterScale = 2900f;
    [Tooltip("가장자리 솜털(침식) 노이즈 크기")]
    public float detailScale = 260f;
    [Tooltip("가장자리 침식 세기 — 클수록 뭉게뭉게 깎인 가장자리")]
    [Range(0f, 1f)] public float erosion = 0.45f;
    [Tooltip("바람 (m/s, x/z)")]
    public Vector2 wind = new Vector2(9f, 4f);

    [Header("조명")]
    [Tooltip("햇빛 세기")]
    [Range(0f, 3f)] public float sunIntensity = 1f;
    [Tooltip("하늘 환경광 세기 (구름 그늘 밝기)")]
    [Range(0f, 3f)] public float ambientIntensity = 1f;
    [Tooltip("다중 산란 — 구름 속까지 빛이 번져 부드럽고 밝은 솜 같은 질감")]
    [Range(0f, 1f)] public float multiScattering = 0.6f;
    [Tooltip("태양 쪽을 볼 때 가장자리 은빛 테두리 (전방 산란)")]
    [Range(0f, 0.95f)] public float silverLining = 0.75f;
    [Tooltip("구름 아랫면의 어두움")]
    [Range(0f, 1f)] public float baseDarkening = 0.45f;
    public Color cloudTint = Color.white;

    [Header("원경")]
    [Tooltip("OceanSurface 가 있으면 그 연무색을 따른다")]
    public Color hazeColor = new Color(0.66f, 0.75f, 0.82f);
    [Tooltip("이 거리에서 구름이 연무색으로 약 63% 녹아든다 (m)")]
    public float hazeDistance = 9000f;
    public float maxDistance = 16000f;

    [Header("품질")]
    [Tooltip("레이마칭 최대 스텝 (높을수록 매끈, 무거움)")]
    [Range(48, 192)] public int maxSteps = 112;

    const int N = 64;
    static Texture3D s_noise;
    static Mesh s_tri;
    Material _mat;
    Task<Color32[]> _gen;

    void OnEnable()
    {
        var sh = Shader.Find("Custom/CloudVolume");
        if (sh == null) { Debug.LogWarning("[CloudLayer] Custom/CloudVolume 셰이더 없음"); return; }
        _mat = new Material(sh) { name = "CloudVolume (runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (s_noise == null) s_noise = Resources.Load<Texture3D>("Env/CloudNoise3D");
        if (s_noise == null && _gen == null) _gen = Task.Run(() => GenerateNoise(N));
        if (s_tri == null)
        {
            s_tri = new Mesh { name = "CloudFullscreenTri", hideFlags = HideFlags.HideAndDontSave };
            s_tri.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 3f, 0f), new Vector3(3f, -1f, 0f) };
            s_tri.triangles = new[] { 0, 1, 2 };
            s_tri.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        if (_mat != null) { if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat); }
        _mat = null;
    }

#if UNITY_EDITOR
    // 에디터: 노이즈 생성이 끝나면 텍스처를 만들고 뷰를 다시 그린다
    void EditorTick()
    {
        if (TryFinishNoise()) UnityEditor.SceneView.RepaintAll();
    }
#endif

    void Update()
    {
        TryFinishNoise();
        if (Application.isPlaying)
        {
            var cam = Camera.main;
            var data = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
            if (data != null && !data.requiresDepthTexture) data.requiresDepthTexture = true;
        }
    }

    bool TryFinishNoise()
    {
        if (s_noise != null || _gen == null || !_gen.IsCompleted) return false;
        s_noise = new Texture3D(N, N, N, TextureFormat.RGBA32, true)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "CloudNoise3D", hideFlags = HideFlags.HideAndDontSave };
        s_noise.SetPixels32(_gen.Result);
        s_noise.Apply(true, true);
        _gen = null;
        return true;
    }

    static bool ShouldRender(Camera cam)
    {
        if (cam.cameraType == CameraType.SceneView) return true;
        return cam.cameraType == CameraType.Game && cam == Camera.main;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_mat == null || s_noise == null || !ShouldRender(cam)) return;
        ApplyMaterial();
        var rp = new RenderParams(_mat)
        {
            camera = cam,
            layer = gameObject.layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            worldBounds = new Bounds(cam.transform.position, Vector3.one * 1e5f),
        };
        // 정점 셰이더가 클립 좌표를 직접 출력하므로 행렬은 무관
        Graphics.RenderMesh(rp, s_tri, 0, Matrix4x4.identity);
    }

    void ApplyMaterial()
    {
        _mat.SetTexture("_Noise3D", s_noise);
        _mat.SetFloat("_CloudBase", cloudBase);
        _mat.SetFloat("_CloudTop", Mathf.Max(cloudTop, cloudBase + 50f));
        _mat.SetFloat("_Coverage", coverage);
        _mat.SetFloat("_Extinction", extinction);
        _mat.SetFloat("_ShapeScale", Mathf.Max(shapeScale, 1f));
        _mat.SetFloat("_ClusterScale", Mathf.Max(clusterScale, 1f));
        _mat.SetFloat("_DetailScale", Mathf.Max(detailScale, 1f));
        _mat.SetFloat("_Erosion", erosion);
        _mat.SetVector("_Wind", new Vector4(wind.x, 0f, wind.y, 0f));
        _mat.SetFloat("_SunIntensity", sunIntensity);
        _mat.SetFloat("_AmbientIntensity", ambientIntensity);
        _mat.SetFloat("_MultiScatter", multiScattering);
        _mat.SetFloat("_SilverLining", silverLining);
        _mat.SetFloat("_BaseDarkening", baseDarkening);
        _mat.SetColor("_CloudTint", cloudTint);
        _mat.SetColor("_HazeColor", OceanSurface.SharedHaze ?? hazeColor);
        _mat.SetFloat("_HazeDistance", Mathf.Max(hazeDistance, 1f));
        _mat.SetFloat("_MaxDist", maxDistance);
        _mat.SetInt("_MaxSteps", maxSteps);
    }

    // ── 노이즈 생성 (스레드 안전: Unity API 미사용) ───────────────────────────
    public static Color32[] GenerateNoise(int n)
    {
        var px = new Color32[n * n * n];
        var rng = new System.Random(1337);
        float[] pts4 = FeaturePoints(4, rng), pts8 = FeaturePoints(8, rng), pts16 = FeaturePoints(16, rng), pts32 = FeaturePoints(32, rng);
        Parallel.For(0, n, z =>
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, w = (z + 0.5f) / n;
                    float perlin = PerlinFbm(u, v, w, 4, 3);
                    float w4 = 1f - Worley(u, v, w, 4, pts4);
                    float w8 = 1f - Worley(u, v, w, 8, pts8);
                    float w16 = 1f - Worley(u, v, w, 16, pts16);
                    float w32 = 1f - Worley(u, v, w, 32, pts32);
                    float wfbm = w4 * 0.625f + w8 * 0.25f + w16 * 0.125f;
                    // Perlin-Worley: 펄린을 워리로 다듬어 뭉게뭉게한 덩어리
                    float pw = Remap(perlin, wfbm - 1f, 1f, 0f, 1f);
                    px[x + y * n + z * n * n] = new Color32(B(pw), B(w8), B(w16), B(w32));
                }
        });
        return px;
    }

    static byte B(float f) => (byte)Mathf.Clamp(Mathf.RoundToInt(f * 255f), 0, 255);
    static float Remap(float v, float lo, float hi, float a, float b) => a + (v - lo) / Mathf.Max(hi - lo, 1e-4f) * (b - a);

    static float[] FeaturePoints(int cells, System.Random rng)
    {
        var p = new float[cells * cells * cells * 3];
        for (int i = 0; i < p.Length; i++) p[i] = (float)rng.NextDouble();
        return p;
    }

    // 타일링 Worley: 셀 주기 경계를 감싸서 이어붙임
    static float Worley(float u, float v, float w, int cells, float[] pts)
    {
        float fx = u * cells, fy = v * cells, fz = w * cells;
        int cx = (int)fx, cy = (int)fy, cz = (int)fz;
        float best = 9f;
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = cx + dx, iy = cy + dy, iz = cz + dz;
                    int wx = (ix % cells + cells) % cells, wy = (iy % cells + cells) % cells, wz = (iz % cells + cells) % cells;
                    int k = (wx + wy * cells + wz * cells * cells) * 3;
                    float px = ix + pts[k], py = iy + pts[k + 1], pz = iz + pts[k + 2];
                    float ddx = px - fx, ddy = py - fy, ddz = pz - fz;
                    float d = ddx * ddx + ddy * ddy + ddz * ddz;
                    if (d < best) best = d;
                }
        return Mathf.Clamp01(Mathf.Sqrt(best));
    }

    // 타일링 값 노이즈 fbm (주기 = period * 2^octave)
    static float PerlinFbm(float u, float v, float w, int period, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            int p = period << o;
            sum += amp * ValueNoise(u * p, v * p, w * p, p);
            norm += amp; amp *= 0.5f;
        }
        return sum / norm;
    }

    static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967295f;
        }
    }

    static float ValueNoise(float x, float y, float z, int p)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fy = y - iy, fz = z - iz;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
        float c(int a, int b, int d) => Hash(((ix + a) % p + p) % p, ((iy + b) % p + p) % p, ((iz + d) % p + p) % p);
        float x00 = Mathf.Lerp(c(0, 0, 0), c(1, 0, 0), fx), x10 = Mathf.Lerp(c(0, 1, 0), c(1, 1, 0), fx);
        float x01 = Mathf.Lerp(c(0, 0, 1), c(1, 0, 1), fx), x11 = Mathf.Lerp(c(0, 1, 1), c(1, 1, 1), fx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
    }
}
