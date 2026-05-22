using UnityEditor;
using UnityEngine;

public class CreateExplosionPrefab
{
    public static void Execute()
    {
        // Create Materials
        Material fireMat = new Material(Shader.Find("Mobile/Particles/Additive"));
        Texture2D fireTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/ExplosionFire.png");
        fireMat.mainTexture = fireTex;
        AssetDatabase.CreateAsset(fireMat, "Assets/Textures/ExplosionFireMat.mat");

        Material smokeMat = new Material(Shader.Find("Mobile/Particles/Alpha Blended"));
        Texture2D smokeTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/ExplosionSmoke.png");
        smokeMat.mainTexture = smokeTex;
        AssetDatabase.CreateAsset(smokeMat, "Assets/Textures/ExplosionSmokeMat.mat");

        // Create Root GameObject
        GameObject root = new GameObject("ExplosionEffectPrefab");
        
        // Main Fire Particle System
        ParticleSystem psFire = root.AddComponent<ParticleSystem>();
        var mainFire = psFire.main;
        mainFire.duration = 1.0f;
        mainFire.loop = false;
        mainFire.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        mainFire.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
        mainFire.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
        mainFire.startColor = new Color(1f, 0.8f, 0.5f, 1f);
        mainFire.playOnAwake = true;

        var emFire = psFire.emission;
        emFire.rateOverTime = 0;
        emFire.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 15, 25) });

        var shapeFire = psFire.shape;
        shapeFire.shapeType = ParticleSystemShapeType.Sphere;
        shapeFire.radius = 1.5f;

        var colFire = psFire.colorOverLifetime;
        colFire.enabled = true;
        Gradient gradFire = new Gradient();
        gradFire.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(new Color(1f, 0.5f, 0f), 0.5f), new GradientColorKey(Color.black, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colFire.color = gradFire;

        var sizeFire = psFire.sizeOverLifetime;
        sizeFire.enabled = true;
        AnimationCurve curveFire = new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.5f));
        sizeFire.size = new ParticleSystem.MinMaxCurve(1.0f, curveFire);

        var rendFire = psFire.GetComponent<ParticleSystemRenderer>();
        rendFire.material = fireMat;

        // Smoke Child
        GameObject smokeObj = new GameObject("Smoke");
        smokeObj.transform.SetParent(root.transform);
        smokeObj.transform.localPosition = Vector3.zero;
        ParticleSystem psSmoke = smokeObj.AddComponent<ParticleSystem>();
        var mainSmoke = psSmoke.main;
        mainSmoke.duration = 2.0f;
        mainSmoke.loop = false;
        mainSmoke.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.0f);
        mainSmoke.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        mainSmoke.startSize = new ParticleSystem.MinMaxCurve(4f, 8f);
        mainSmoke.startColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);
        mainSmoke.playOnAwake = true;

        var emSmoke = psSmoke.emission;
        emSmoke.rateOverTime = 0;
        emSmoke.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.1f, 10, 20) });

        var shapeSmoke = psSmoke.shape;
        shapeSmoke.shapeType = ParticleSystemShapeType.Sphere;
        shapeSmoke.radius = 2.0f;

        var colSmoke = psSmoke.colorOverLifetime;
        colSmoke.enabled = true;
        Gradient gradSmoke = new Gradient();
        gradSmoke.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.black, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colSmoke.color = gradSmoke;

        var sizeSmoke = psSmoke.sizeOverLifetime;
        sizeSmoke.enabled = true;
        AnimationCurve curveSmoke = new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 2.0f));
        sizeSmoke.size = new ParticleSystem.MinMaxCurve(1.0f, curveSmoke);

        var rendSmoke = psSmoke.GetComponent<ParticleSystemRenderer>();
        rendSmoke.material = smokeMat;

        // Save Prefab
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/ExplosionEffect.prefab");
        Object.DestroyImmediate(root);
    }
}
