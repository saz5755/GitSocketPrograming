using UnityEditor;
using UnityEngine;

public class ApplyKF21Materials
{
    public static void Execute()
    {
        // We already extracted materials using SearchAndRemapMaterials.
        // Now we need to assign the textures to the extracted materials.
        
        string[] matNames = {
            "cock_2.005", "cock_2.006", "cock_2.007", "cock_2.008", "cock_2.009",
            "ins.001", "object_0", "object_1", "object_2", "object_3", "object_4", "wheel.001"
        };

        string[] texNames = {
            "cock_2.0_baseColor", "cock_2.1_baseColor", "cock_2.2_baseColor", "cock_2.3_baseColor", "cock_2.4_baseColor",
            "ins.6_baseColor", "object_0_baseColor", "object_1_baseColor", "object_2_baseColor", "object_3_baseColor", "object_4_baseColor", "wheel.0_baseColor"
        };

        for (int i = 0; i < matNames.Length; i++)
        {
            string matPath = $"Assets/Models/KF21/Materials/{matNames[i]}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null)
            {
                // Find texture
                string texPathPng = $"Assets/Models/KF21/Textures/{texNames[i]}.png";
                string texPathJpeg = $"Assets/Models/KF21/Textures/{texNames[i]}.jpeg";
                
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPathPng);
                if (tex == null) tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPathJpeg);

                if (tex != null)
                {
                    mat.mainTexture = tex;
                    
                    // Check for normal map
                    string normalPathPng = $"Assets/Models/KF21/Textures/{texNames[i].Replace("_baseColor", "_normal")}.png";
                    Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPathPng);
                    if (normalTex != null)
                    {
                        mat.EnableKeyword("_NORMALMAP");
                        mat.SetTexture("_BumpMap", normalTex);
                    }

                    // Check for emissive map
                    string emissivePathJpeg = $"Assets/Models/KF21/Textures/{texNames[i].Replace("_baseColor", "_emissive")}.jpeg";
                    Texture2D emissiveTex = AssetDatabase.LoadAssetAtPath<Texture2D>(emissivePathJpeg);
                    if (emissiveTex != null)
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetTexture("_EmissionMap", emissiveTex);
                        mat.SetColor("_EmissionColor", Color.white);
                    }

                    EditorUtility.SetDirty(mat);
                    Debug.Log($"Assigned textures to {matNames[i]}");
                }
            }
        }
        AssetDatabase.SaveAssets();
    }
}
