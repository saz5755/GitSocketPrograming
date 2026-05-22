using UnityEngine;
using UnityEngine.Rendering;

// 플레어 디코이에 부착되는 시각 효과 컴포넌트
// CountermeasureSystem.Deploy()에서 FlareDecoyVFX.Attach(go, lifetime) 호출
public class FlareDecoyVFX : MonoBehaviour
{
    Material _coreMat;
    Material _haloMat;
    float    _age;
    float    _lifetime;

    // ── 팩토리 ────────────────────────────────────────────────────────────
    public static void Attach(GameObject host, float lifetime)
    {
        host.AddComponent<FlareDecoyVFX>().InitVFX(lifetime);
    }

    // ── 초기화 ────────────────────────────────────────────────────────────
    void InitVFX(float lifetime)
    {
        _lifetime = lifetime;
        float seed = Random.Range(0f, 6.28f);

        _coreMat = BuildGlowSphere("FlareCore", 2.2f, seed);
        _haloMat = BuildGlowSphere("FlareHalo", 5.5f, seed + 1.1f);
        BuildSmokeTrail();
    }

    // ── 발광 구체 생성 ────────────────────────────────────────────────────
    Material BuildGlowSphere(string goName, float scale, float seed)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = goName;
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * scale;
        Object.Destroy(go.GetComponent<SphereCollider>());

        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows    = false;

        var sh = Shader.Find("Custom/FlareEffect");
        if (sh == null) { r.enabled = false; return null; }

        var mat = new Material(sh);
        mat.SetFloat("_Age",  0f);
        mat.SetFloat("_Seed", seed);
        r.sharedMaterial = mat;
        return mat;
    }

    // ── 연기 트레일 ───────────────────────────────────────────────────────
    void BuildSmokeTrail()
    {
        var tr = gameObject.AddComponent<TrailRenderer>();
        tr.time              = 2.2f;
        tr.startWidth        = 0.7f;
        tr.endWidth          = 0.08f;
        tr.minVertexDistance = 0.4f;
        tr.textureMode       = LineTextureMode.Stretch;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows    = false;

        var sh = Shader.Find("Custom/SmokeTrail");
        Material mat;
        if (sh != null)
        {
            mat = new Material(sh);
            mat.SetColor("_Color",      new Color(0.95f, 0.93f, 0.90f, 0.65f));
            mat.SetFloat("_NoiseScale", 2.2f);
        }
        else
        {
            var fallback = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                        ?? Shader.Find("Sprites/Default");
            mat = new Material(fallback);
            mat.color = new Color(0.92f, 0.90f, 0.88f, 0.55f);
        }
        tr.material = mat;

        var gc = new Gradient();
        gc.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.00f, 0.96f, 0.88f), 0.00f),
                new GradientColorKey(new Color(0.88f, 0.85f, 0.82f), 0.25f),
                new GradientColorKey(new Color(0.58f, 0.56f, 0.54f), 1.00f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.80f, 0.00f),
                new GradientAlphaKey(0.45f, 0.45f),
                new GradientAlphaKey(0.00f, 1.00f),
            });
        tr.colorGradient = gc;
    }

    // ── 수명 업데이트 ─────────────────────────────────────────────────────
    void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / _lifetime);
        _coreMat?.SetFloat("_Age", t);
        _haloMat?.SetFloat("_Age", t);
    }
}
