using UnityEngine;

public class BulletImpactEffect : MonoBehaviour
{
    public static void Spawn(Vector3 worldPos)
    {
        var go = new GameObject("BulletImpact");
        go.transform.position = worldPos;
        go.AddComponent<BulletImpactEffect>().Init();
        Destroy(go, 2.0f);
    }

    void Init()
    {
        Material addMat = Resources.Load<Material>("VFX/Mat_Additive");
        Material alphaMat = Resources.Load<Material>("VFX/Mat_Alpha");

        // 1. Sharp Core Flash (Very quick, intense)
        var flash = MakeParticles("Flash", 1);
        var fm = flash.main;
        fm.startLifetime = 0.03f;
        fm.startSpeed = 0f;
        fm.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        fm.startColor = new Color(1f, 1f, 0.8f, 1f);
        fm.loop = false;
        var fe = flash.emission;
        fe.rateOverTime = 0f;
        fe.SetBurst(0, new ParticleSystem.Burst(0f, 1));
        var fr = flash.GetComponent<ParticleSystemRenderer>();
        fr.renderMode = ParticleSystemRenderMode.Billboard;
        if (addMat != null) fr.material = addMat;
        flash.Play();

        // 2. Steel Cutting Sparks (More visible, bright streaks)
        var sparks = MakeParticles("Sparks", 60);
        var sm = sparks.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(20f, 50f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f); // Made thicker so they are visible
        sm.startColor = new Color(1f, 0.9f, 0.5f, 1f);
        sm.gravityModifier = 1.5f;
        sm.loop = false;
        
        var se = sparks.emission;
        se.rateOverTime = 0f;
        se.SetBurst(0, new ParticleSystem.Burst(0f, 60));
        
        var ss = sparks.shape;
        ss.enabled = true;
        ss.shapeType = ParticleSystemShapeType.Hemisphere; // Hemisphere to bounce off surface
        ss.radius = 0.1f;
        
        var scol = sparks.colorOverLifetime;
        scol.enabled = true;
        Gradient sgrad = new Gradient();
        sgrad.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(Color.white, 0f), 
                new GradientColorKey(new Color(1f, 0.7f, 0.1f), 0.3f), 
                new GradientColorKey(new Color(1f, 0.2f, 0f), 0.7f), 
                new GradientColorKey(Color.black, 1f) 
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(1f, 0f), 
                new GradientAlphaKey(1f, 0.8f), 
                new GradientAlphaKey(0f, 1f) 
            }
        );
        scol.color = sgrad;

        var sr = sparks.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch;
        sr.lengthScale = 6f; // Adjusted length scale
        sr.velocityScale = 0.05f;
        if (addMat != null) sr.material = addMat;
        sparks.Play();

        // 3. Micro Sparks (Glowing chunks)
        var microSparks = MakeParticles("MicroSparks", 30);
        var msm = microSparks.main;
        msm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        msm.startSpeed = new ParticleSystem.MinMaxCurve(10f, 25f);
        msm.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.25f); // Made thicker
        msm.startColor = new Color(1f, 0.8f, 0.3f, 1f);
        msm.gravityModifier = 1.2f;
        msm.loop = false;
        
        var mse = microSparks.emission;
        mse.rateOverTime = 0f;
        mse.SetBurst(0, new ParticleSystem.Burst(0f, 30));
        
        var mss = microSparks.shape;
        mss.enabled = true;
        mss.shapeType = ParticleSystemShapeType.Hemisphere;
        mss.radius = 0.1f;

        var msr = microSparks.GetComponent<ParticleSystemRenderer>();
        msr.renderMode = ParticleSystemRenderMode.Stretch;
        msr.lengthScale = 2f;
        msr.velocityScale = 0.05f;
        if (addMat != null) msr.material = addMat;
        microSparks.Play();

        // 4. Subtle Smoke Puff (Reduced to not hide the sparks)
        var smoke = MakeParticles("Smoke", 3);
        var km = smoke.main;
        km.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        km.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        km.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.5f);
        km.startColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);
        km.gravityModifier = -0.05f;
        km.loop = false;
        var ke = smoke.emission;
        ke.rateOverTime = 0f;
        ke.SetBurst(0, new ParticleSystem.Burst(0f, 3));
        var ks = smoke.shape;
        ks.enabled = true;
        ks.shapeType = ParticleSystemShapeType.Sphere;
        ks.radius = 0.1f;
        
        var ksocal = smoke.colorOverLifetime;
        ksocal.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.5f, 0.5f, 0.5f), 0f), new GradientColorKey(new Color(0.2f, 0.2f, 0.2f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.2f), new GradientAlphaKey(0f, 1f) });
        ksocal.color = grad;

        var ksize = smoke.sizeOverLifetime;
        ksize.enabled = true;
        ksize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.5f)));

        var kr = smoke.GetComponent<ParticleSystemRenderer>();
        kr.renderMode = ParticleSystemRenderMode.Billboard;
        if (alphaMat != null) kr.material = alphaMat;
        smoke.Play();
    }

    ParticleSystem MakeParticles(string name, int maxParticles)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var ps = child.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }
}
