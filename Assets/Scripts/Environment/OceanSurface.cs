using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 바다 표면. 씬의 빈 GameObject 에 부착하고 Inspector 에서 항모를 지정한다.
///  · [ExecuteAlways] — 플레이하지 않아도 씬/게임 뷰에 그려지고 Inspector 값이 즉시 반영된다
///  · 카메라 기준 극좌표 격자 메쉬(가까울수록 촘촘) + Custom/Ocean 셰이더. 격자 위치는 셰이더가 카메라마다 계산하므로
///    게임 오브젝트를 만들지 않고 Graphics.RenderMesh 로 각 카메라에 그린다 (하이어라키·씬 저장에 흔적 없음)
///  · 항모는 게임플레이상 정지해 있지만, 물결이 함정 진행 반대 방향으로 흘러 항해하는 것처럼 보이고
///    선미 뒤로 Kelvin V자 항적이 생긴다 (virtualSpeed)
///  · 플레이 중에만: 카메라 원거리 클립 확장, 스카이박스 지면색을 연무색으로 (복제본만 수정 — 에셋/씬은 그대로)
/// 충돌체는 만들지 않는다 (게임플레이 영향 없음).
/// </summary>
[ExecuteAlways]
public class OceanSurface : MonoBehaviour
{
    [Header("수면")]
    [Tooltip("해수면 높이 (월드 Y). 항모 흘수선에 맞춤")]
    public float seaLevel = -11f;
    [Tooltip("너울 높이 배율 (1 = 보통 해상, 0.5 = 잔잔, 2 = 거친 바다)")]
    [Range(0f, 3f)] public float waveScale = 1f;
    [Tooltip("파도 마루의 뾰족함 (0 = 둥근 사인파, 1 = 기본, 2 = 날카로운 마루)")]
    [Range(0f, 2f)] public float choppiness = 1f;
    [Tooltip("바람 잔물결 세기")]
    [Range(0f, 2f)] public float rippleStrength = 1f;
    [Tooltip("수백 m 단위로 거칠고 잔잔한 해면이 얼룩지는 정도")]
    [Range(0f, 1f)] public float windPatches = 0.6f;
    [Tooltip("파도 마루 흰 포말 양")]
    [Range(0f, 2f)] public float foamAmount = 1f;

    [Header("색")]
    public Color deepColor = new Color(0.012f, 0.045f, 0.085f);
    [Tooltip("파도 마루를 투과한 햇빛 색")]
    public Color crestScatterColor = new Color(0.030f, 0.165f, 0.190f);
    public Color foamColor = new Color(0.92f, 0.95f, 0.97f);
    [Tooltip("수면에 반사되는 하늘 천정 색")]
    public Color skyZenithColor = new Color(0.24f, 0.42f, 0.68f);

    [Header("항모 항적")]
    public Transform carrier;
    [Tooltip("항모 로컬 기준 선미 위치")]
    public Vector3 sternLocal = new Vector3(0f, 0f, 94f);
    [Tooltip("항모 로컬 기준 진행 방향 (선수 쪽)")]
    public Vector3 forwardLocal = Vector3.back;
    [Tooltip("선체 반길이 / 흘수선 반폭 (m)")]
    public Vector2 hullHalfSize = new Vector2(94f, 13f);
    [Tooltip("연출용 항해 속도 (m/s) — 물결 흐름과 항적 파장에 사용. 실제 항모 위치는 움직이지 않는다")]
    [Range(0f, 25f)] public float virtualSpeed = 12f;
    [Tooltip("항적이 이어지는 거리 (m)")]
    public float wakeLength = 650f;
    [Tooltip("항적 물결·포말 세기")]
    [Range(0f, 2f)] public float wakeStrength = 1f;

    [Header("원경")]
    [Tooltip("스카이박스의 실제 수평선 색을 측정해 연무색으로 사용 (바다·구름이 하늘과 이음새 없이 이어짐). 끄면 아래 hazeColor 사용")]
    public bool autoHazeFromSky = true;
    public Color hazeColor = new Color(0.66f, 0.75f, 0.82f);
    public float hazeStart = 600f;
    public float hazeEnd = 14000f;
    [Tooltip("플레이 중 메인 카메라 원거리 클립 (바다가 수평선까지 이어지도록 확장)")]
    public float cameraFar = 20000f;

    /// <summary>다른 환경 요소(구름)가 같은 연무색을 쓰도록 공유</summary>
    public static Color? SharedHaze;

    // 링 간격(반지름 비례)과 원주 간격이 비슷하도록 → 삼각형이 정사각에 가까워 어느 방향으로도 고르게 표현
    const int Rings = 400, Segments = 256;
    const float InnerRadius = 0.6f, OuterRadius = 18000f;
    static readonly float GridK = Mathf.Max(Mathf.Log(OuterRadius / InnerRadius) / Rings, 2f * Mathf.PI / Segments);

    static Mesh s_mesh;
    Material _mat;
    Material _skyClone, _skyOriginal;
    Color _haze;
    bool _needSample = true;

    static readonly int ID_ShipPos = Shader.PropertyToID("_ShipPos");
    static readonly int ID_ShipFwd = Shader.PropertyToID("_ShipFwd");
    static readonly int ID_ShipSpeed = Shader.PropertyToID("_ShipSpeed");
    static readonly int ID_ShipHalf = Shader.PropertyToID("_ShipHalf");
    static readonly int ID_WakeLength = Shader.PropertyToID("_WakeLength");
    static readonly int ID_WakeStrength = Shader.PropertyToID("_WakeStrength");
    static readonly int ID_Haze = Shader.PropertyToID("_HazeColor");
    static readonly int ID_HazeStart = Shader.PropertyToID("_HazeStart");
    static readonly int ID_HazeEnd = Shader.PropertyToID("_HazeEnd");
    static readonly int ID_WaveScale = Shader.PropertyToID("_WaveScale");
    static readonly int ID_Choppiness = Shader.PropertyToID("_Choppiness");
    static readonly int ID_Ripple = Shader.PropertyToID("_RippleStrength");
    static readonly int ID_Patches = Shader.PropertyToID("_WindPatches");
    static readonly int ID_FoamAmount = Shader.PropertyToID("_FoamAmount");
    static readonly int ID_Deep = Shader.PropertyToID("_DeepColor");
    static readonly int ID_Shallow = Shader.PropertyToID("_ShallowColor");
    static readonly int ID_Foam = Shader.PropertyToID("_FoamColor");
    static readonly int ID_Zenith = Shader.PropertyToID("_ZenithColor");
    static readonly int ID_SeaLevel = Shader.PropertyToID("_SeaLevel");
    static readonly int ID_GridK = Shader.PropertyToID("_GridK");
    static readonly int ID_Outer = Shader.PropertyToID("_OuterRadius");

    void OnEnable()
    {
        var sh = Shader.Find("Custom/Ocean");
        if (sh == null) { Debug.LogWarning("[OceanSurface] Custom/Ocean 셰이더 없음"); return; }
        _mat = new Material(sh) { name = "Ocean (runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (s_mesh == null) s_mesh = BuildPolarGrid();
        _haze = hazeColor;
        SharedHaze = _haze;
        _needSample = true;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (_skyClone != null)
        {
            if (RenderSettings.skybox == _skyClone) RenderSettings.skybox = _skyOriginal;
            DestroySafe(_skyClone);
            _skyClone = null;
        }
        DestroySafe(_mat);
        _mat = null;
    }

    void OnValidate() => _needSample = true;

    void Update()
    {
        if (_mat == null) return;
        if (_needSample)
        {
            _needSample = false;
            _haze = autoHazeFromSky ? SampleSkyHorizon(hazeColor) : hazeColor;
            SharedHaze = _haze;
            if (Application.isPlaying) ApplySkyGround();
        }
        if (Application.isPlaying)
        {
            var cam = Camera.main;
            if (cam != null && cam.farClipPlane < cameraFar) cam.farClipPlane = cameraFar;
        }
    }

    // 스카이박스 지면색도 연무색으로 (스커트 틈새로 혹시 보이더라도 튀지 않게). 플레이 중 복제본에만 적용
    void ApplySkyGround()
    {
        var sky = _skyClone != null ? _skyOriginal : RenderSettings.skybox;
        if (sky == null || !sky.HasProperty("_GroundColor")) return;
        if (_skyClone == null)
        {
            _skyOriginal = sky;
            _skyClone = new Material(sky) { name = sky.name + " (ocean)", hideFlags = HideFlags.HideAndDontSave };
            RenderSettings.skybox = _skyClone;
        }
        _skyClone.SetColor("_GroundColor", _haze);
    }

    static bool ShouldRender(Camera cam)
    {
        if (cam.cameraType == CameraType.SceneView) return true;
        return cam.cameraType == CameraType.Game && cam == Camera.main;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_mat == null || s_mesh == null || !ShouldRender(cam)) return;
        ApplyMaterial();
        var rp = new RenderParams(_mat)
        {
            camera = cam,
            layer = gameObject.layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            worldBounds = new Bounds(cam.transform.position, Vector3.one * (OuterRadius * 2.2f)),
        };
        Graphics.RenderMesh(rp, s_mesh, 0, Matrix4x4.identity);
    }

    void ApplyMaterial()
    {
        if (carrier != null)
        {
            Vector3 stern = carrier.TransformPoint(sternLocal);
            Vector3 fwd = carrier.TransformDirection(forwardLocal); fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            _mat.SetVector(ID_ShipPos, new Vector4(stern.x, stern.y, stern.z, 0f));
            _mat.SetVector(ID_ShipFwd, new Vector4(fwd.x, 0f, fwd.z, 0f));
            _mat.SetFloat(ID_ShipSpeed, Mathf.Max(0.5f, virtualSpeed));
            _mat.SetFloat(ID_WakeStrength, wakeStrength);
        }
        else
        {
            _mat.SetVector(ID_ShipPos, new Vector4(0f, 0f, 1e6f, 0f));   // 항적 없음
            _mat.SetVector(ID_ShipFwd, new Vector4(0f, 0f, -1f, 0f));
            _mat.SetFloat(ID_ShipSpeed, 0.5f);
            _mat.SetFloat(ID_WakeStrength, 0f);
        }
        _mat.SetVector(ID_ShipHalf, new Vector4(hullHalfSize.x, hullHalfSize.y, 0f, 0f));
        _mat.SetFloat(ID_WakeLength, wakeLength);
        _mat.SetColor(ID_Haze, _haze);
        _mat.SetFloat(ID_HazeStart, hazeStart);
        _mat.SetFloat(ID_HazeEnd, hazeEnd);
        _mat.SetFloat(ID_WaveScale, waveScale);
        _mat.SetFloat(ID_Choppiness, choppiness);
        _mat.SetFloat(ID_Ripple, rippleStrength);
        _mat.SetFloat(ID_Patches, windPatches);
        _mat.SetFloat(ID_FoamAmount, foamAmount);
        _mat.SetColor(ID_Deep, deepColor);
        _mat.SetColor(ID_Shallow, crestScatterColor);
        _mat.SetColor(ID_Foam, foamColor);
        _mat.SetColor(ID_Zenith, skyZenithColor);
        _mat.SetFloat(ID_SeaLevel, seaLevel);
        _mat.SetFloat(ID_GridK, GridK);
        _mat.SetFloat(ID_Outer, OuterRadius);
    }

    // 메인 카메라 설정을 그대로 복제한 임시 카메라로 스카이박스만 그려, 수평선 바로 위 색(포스트 이전, 선형)을 측정한다.
    // 화면 세로 중앙 행이 정확히 수평선(0°)이 되도록 피치 0 으로 찍고, 중앙보다 2픽셀 위를 읽는다.
    static Color SampleSkyHorizon(Color fallback)
    {
        if (RenderSettings.skybox == null) return fallback;
        const int S = 64;
        var rt = RenderTexture.GetTemporary(S, S, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var go = new GameObject("SkyProbeCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var main = Camera.main;
        if (main != null) cam.CopyFrom(main);
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.cullingMask = 0;
        cam.fieldOfView = 20f;
        cam.targetTexture = rt;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = false;
        var tex = new Texture2D(S, S, TextureFormat.RGBAHalf, false, true);
        Color sum = Color.black;
        int n = 0;
        var prev = RenderTexture.active;
        try
        {
            for (int k = 0; k < 8; k++)
            {
                go.transform.SetPositionAndRotation(main != null ? main.transform.position : Vector3.zero, Quaternion.Euler(0f, k * 45f, 0f));
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, S, S), 0, 0);
                tex.Apply();
                for (int x = S / 2 - 8; x < S / 2 + 8; x++) { sum += tex.GetPixel(x, S / 2 + 2); n++; }
            }
        }
        catch { return fallback; }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            DestroySafe(tex);
            DestroySafe(go);
        }
        var c = sum / Mathf.Max(1, n);
        if (c.maxColorComponent <= 0.001f) return fallback;
        c.a = 1f;
        return c;
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    // 극좌표 격자: 중심 근처는 촘촘, 바깥은 지수적으로 성기게 — 화면상 삼각형 크기가 대체로 균일
    static Mesh BuildPolarGrid()
    {
        var v = new Vector3[(Rings + 1) * Segments + 1];
        v[0] = Vector3.zero;
        float k = Mathf.Log(OuterRadius / InnerRadius) / Rings;
        for (int r = 0; r <= Rings; r++)
        {
            float rad = InnerRadius * Mathf.Exp(k * r);
            // 링마다 반 칸씩 엇갈려 삼각형 대각선 방향이 한쪽으로 줄지어 보이지 않게
            float off = (r & 1) * 0.5f;
            for (int s = 0; s < Segments; s++)
            {
                float a = (s + off) / Segments * Mathf.PI * 2f;
                v[1 + r * Segments + s] = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
            }
        }
        var t = new int[Segments * 3 + Rings * Segments * 6];
        int n = 0;
        for (int s = 0; s < Segments; s++)
        {
            t[n++] = 0; t[n++] = 1 + (s + 1) % Segments; t[n++] = 1 + s;
        }
        for (int r = 0; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                int a = 1 + r * Segments + s, b = 1 + r * Segments + (s + 1) % Segments;
                int c = a + Segments, d = b + Segments;
                t[n++] = a; t[n++] = b; t[n++] = c;
                t[n++] = b; t[n++] = d; t[n++] = c;
            }
        var m = new Mesh { name = "OceanPolarGrid", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
        m.vertices = v;
        m.triangles = t;
        m.bounds = new Bounds(Vector3.zero, new Vector3(OuterRadius * 2f, 60f, OuterRadius * 2f));
        m.UploadMeshData(true);
        return m;
    }
}
