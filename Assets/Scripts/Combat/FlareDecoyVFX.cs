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

        // 1. Intense Core Glow (Magnesium Burn)
        GameObject coreObj = new GameObject("FlareCore");
        coreObj.transform.SetParent(transform, false);
        ParticleSystem corePS = coreObj.AddComponent<ParticleSystem>();
        var cMain = corePS.main;
        cMain.duration = lifetime;
        cMain.loop = false;
        cMain.startLifetime = 0.05f;
        cMain.startSpeed = 0f;
        cMain.startSize = 4.0f;
        cMain.startColor = new Color(1f, 1f, 1f, 1f);
        cMain.simulationSpace = ParticleSystemSimulationSpace.World;
        
        var cEm = corePS.emission;
        cEm.rateOverTime = 60f;
        
        var cRend = corePS.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) cRend.material = addMat;

        // 2. Outer Halo (Orange/Red Glare)
        GameObject haloObj = new GameObject("FlareHalo");
        haloObj.transform.SetParent(transform, false);
        ParticleSystem haloPS = haloObj.AddComponent<ParticleSystem>();
        var hMain = haloPS.main;
        hMain.duration = lifetime;
        hMain.loop = false;
        hMain.startLifetime = 0.1f;
        hMain.startSpeed = 0f;
        hMain.startSize = 12.0f;
        hMain.startColor = new Color(1f, 0.4f, 0.1f, 0.8f);
        hMain.simulationSpace = ParticleSystemSimulationSpace.World;
        
        var hEm = haloPS.emission;
        hEm.rateOverTime = 30f;
        
        var hRend = haloPS.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) hRend.material = addMat;

        // 3. Falling Sparks (Burning fragments)
        GameObject sparksObj = new GameObject("FlareSparks");
        sparksObj.transform.SetParent(transform, false);
        ParticleSystem sparksPS = sparksObj.AddComponent<ParticleSystem>();
        var sMain = sparksPS.main;
        sMain.duration = lifetime;
        sMain.loop = false;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 2.0f);
        sMain.startSpeed = new ParticleSystem.MinMaxCurve(5f, 15f);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        sMain.startColor = new Color(1f, 0.9f, 0.5f, 1f);
        sMain.gravityModifier = 1.2f;
        sMain.simulationSpace = ParticleSystemSimulationSpace.World;

        var sEm = sparksPS.emission;
        sEm.rateOverTime = 80f;

        var sShape = sparksPS.shape;
        sShape.shapeType = ParticleSystemShapeType.Sphere;
        sShape.radius = 0.5f;

        var sCol = sparksPS.colorOverLifetime;
        sCol.enabled = true;
        Gradient sGrad = new Gradient();
        sGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0f), 0.6f), new GradientColorKey(Color.black, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        sCol.color = sGrad;

        var sSize = sparksPS.sizeOverLifetime;
        sSize.enabled = true;
        sSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0f)));

        var sRend = sparksPS.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) sRend.material = addMat;
        sRend.renderMode = ParticleSystemRenderMode.Stretch;
        sRend.lengthScale = 3f;
        sRend.velocityScale = 0.05f;

        // 4. Thick Smoke Trail
        GameObject smokeObj = new GameObject("FlareSmoke");
        smokeObj.transform.SetParent(transform, false);
        ParticleSystem smokePS = smokeObj.AddComponent<ParticleSystem>();
        var smMain = smokePS.main;
        smMain.duration = lifetime;
        smMain.loop = false;
        smMain.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
        smMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        smMain.startSize = new ParticleSystem.MinMaxCurve(3f, 8f);
        smMain.startColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);
        smMain.simulationSpace = ParticleSystemSimulationSpace.World;

        var smEm = smokePS.emission;
        smEm.rateOverTime = 40f;

        var smShape = smokePS.shape;
        smShape.shapeType = ParticleSystemShapeType.Sphere;
        smShape.radius = 0.8f;

        var smCol = smokePS.colorOverLifetime;
        smCol.enabled = true;
        Gradient smGrad = new Gradient();
        smGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.95f, 0.9f), 0f), new GradientColorKey(new Color(0.5f, 0.5f, 0.5f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.1f), new GradientAlphaKey(0f, 1f) }
        );
        smCol.color = smGrad;

        var smSize = smokePS.sizeOverLifetime;
        smSize.enabled = true;
        smSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(1, 3f)));

        var smRend = smokePS.GetComponent<ParticleSystemRenderer>();
        if (alphaMat != null) smRend.material = alphaMat;

        // Dynamic Light
        Light light = gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.9f, 0.7f);
        light.intensity = 8f;
        light.range = 50f;

        Destroy(gameObject, lifetime + 6.0f);
    }
}
