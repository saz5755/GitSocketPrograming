using UnityEngine;

public class FlareDecoyVFX : MonoBehaviour
{
    public static void Attach(GameObject host, float lifetime)
    {
        var vfx = host.AddComponent<FlareDecoyVFX>();
        vfx.InitVFX(lifetime);
    }

    void InitVFX(float lifetime)
    {
        Material addMat = Resources.Load<Material>("VFX/Mat_Additive");
        Material alphaMat = Resources.Load<Material>("VFX/Mat_Alpha");

        // 1. Core Glow (Bright White/Yellow)
        GameObject coreObj = new GameObject("FlareCore");
        coreObj.transform.SetParent(transform, false);
        ParticleSystem corePS = coreObj.AddComponent<ParticleSystem>();
        var cMain = corePS.main;
        cMain.duration = lifetime;
        cMain.loop = false;
        cMain.startLifetime = 0.1f;
        cMain.startSpeed = 0f;
        cMain.startSize = 2.5f;
        cMain.startColor = new Color(1f, 0.9f, 0.7f, 1f);
        cMain.simulationSpace = ParticleSystemSimulationSpace.World;
        
        var cEm = corePS.emission;
        cEm.rateOverTime = 30f;
        
        var cRend = corePS.GetComponent<ParticleSystemRenderer>();
        cRend.material = addMat;

        // 2. Halo (Orange/Red)
        GameObject haloObj = new GameObject("FlareHalo");
        haloObj.transform.SetParent(transform, false);
        ParticleSystem haloPS = haloObj.AddComponent<ParticleSystem>();
        var hMain = haloPS.main;
        hMain.duration = lifetime;
        hMain.loop = false;
        hMain.startLifetime = 0.15f;
        hMain.startSpeed = 0f;
        hMain.startSize = 6.0f;
        hMain.startColor = new Color(1f, 0.3f, 0.0f, 0.6f);
        hMain.simulationSpace = ParticleSystemSimulationSpace.World;
        
        var hEm = haloPS.emission;
        hEm.rateOverTime = 20f;
        
        var hRend = haloPS.GetComponent<ParticleSystemRenderer>();
        hRend.material = addMat;

        // 3. Sparks (Falling burning pieces)
        GameObject sparksObj = new GameObject("FlareSparks");
        sparksObj.transform.SetParent(transform, false);
        ParticleSystem sparksPS = sparksObj.AddComponent<ParticleSystem>();
        var sMain = sparksPS.main;
        sMain.duration = lifetime;
        sMain.loop = false;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        sMain.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        sMain.startColor = new Color(1f, 0.8f, 0.4f, 1f);
        sMain.gravityModifier = 0.8f;
        sMain.simulationSpace = ParticleSystemSimulationSpace.World;

        var sEm = sparksPS.emission;
        sEm.rateOverTime = 40f;

        var sShape = sparksPS.shape;
        sShape.shapeType = ParticleSystemShapeType.Sphere;
        sShape.radius = 0.5f;

        var sCol = sparksPS.colorOverLifetime;
        sCol.enabled = true;
        Gradient sGrad = new Gradient();
        sGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.2f, 0f), 0.7f), new GradientColorKey(Color.black, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        sCol.color = sGrad;

        var sSize = sparksPS.sizeOverLifetime;
        sSize.enabled = true;
        sSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0f)));

        var sRend = sparksPS.GetComponent<ParticleSystemRenderer>();
        sRend.material = addMat;
        sRend.renderMode = ParticleSystemRenderMode.Stretch;
        sRend.velocityScale = 0.05f;

        // 4. Smoke Trail
        GameObject smokeObj = new GameObject("FlareSmoke");
        smokeObj.transform.SetParent(transform, false);
        ParticleSystem smokePS = smokeObj.AddComponent<ParticleSystem>();
        var smMain = smokePS.main;
        smMain.duration = lifetime;
        smMain.loop = false;
        smMain.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        smMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        smMain.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
        smMain.startColor = new Color(0.9f, 0.9f, 0.9f, 0.6f);
        smMain.simulationSpace = ParticleSystemSimulationSpace.World;

        var smEm = smokePS.emission;
        smEm.rateOverTime = 30f;

        var smShape = smokePS.shape;
        smShape.shapeType = ParticleSystemShapeType.Sphere;
        smShape.radius = 0.5f;

        var smCol = smokePS.colorOverLifetime;
        smCol.enabled = true;
        Gradient smGrad = new Gradient();
        smGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.9f, 0.8f), 0f), new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.1f), new GradientAlphaKey(0f, 1f) }
        );
        smCol.color = smGrad;

        var smSize = smokePS.sizeOverLifetime;
        smSize.enabled = true;
        smSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 3f)));

        var smRend = smokePS.GetComponent<ParticleSystemRenderer>();
        smRend.material = alphaMat;

        // Light
        Light light = gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.5f);
        light.intensity = 4f;
        light.range = 30f;

        Destroy(gameObject, lifetime + 4.0f);
    }
}
