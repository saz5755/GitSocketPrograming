using UnityEngine;
using UnityEditor;
using System.IO;

public class GenerateVFXAssets
{
    public static void Execute()
    {
        if (!AssetDatabase.IsValidFolder("Assets/VFX")) 
            AssetDatabase.CreateFolder("Assets", "VFX");

        // 1. Soft Particle Texture
        int size = 128;
        Texture2D softTex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(size/2f, size/2f)) / (size/2f);
                float a = Mathf.Clamp01(1f - d);
                a = Mathf.Pow(a, 1.5f); // softer edge
                softTex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }
        softTex.Apply();
        File.WriteAllBytes("Assets/VFX/SoftParticle.png", softTex.EncodeToPNG());

        // 2. Shock Diamond Texture
        Texture2D diamondTex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float u = Mathf.Abs(x - size/2f) / (size/2f);
                float v = Mathf.Abs(y - size/2f) / (size/2f);
                float d = u + v; // Diamond shape distance
                float a = Mathf.Clamp01(1f - d);
                a = Mathf.Pow(a, 2f);
                diamondTex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }
        diamondTex.Apply();
        File.WriteAllBytes("Assets/VFX/ShockDiamond.png", diamondTex.EncodeToPNG());

        AssetDatabase.Refresh();

        // Create Materials
        Texture2D loadedSoft = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VFX/SoftParticle.png");
        Texture2D loadedDiamond = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VFX/ShockDiamond.png");

        Material addMat = new Material(Shader.Find("Mobile/Particles/Additive"));
        addMat.mainTexture = loadedSoft;
        AssetDatabase.CreateAsset(addMat, "Assets/VFX/Mat_Additive.mat");

        Material alphaMat = new Material(Shader.Find("Mobile/Particles/Alpha Blended"));
        alphaMat.mainTexture = loadedSoft;
        AssetDatabase.CreateAsset(alphaMat, "Assets/VFX/Mat_Alpha.mat");

        Material diamondMat = new Material(Shader.Find("Mobile/Particles/Additive"));
        diamondMat.mainTexture = loadedDiamond;
        AssetDatabase.CreateAsset(diamondMat, "Assets/VFX/Mat_ShockDiamond.mat");

        AssetDatabase.SaveAssets();
        Debug.Log("VFX Assets Generated.");
    }
}
