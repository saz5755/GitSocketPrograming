using UnityEditor;
using UnityEngine;

public class CreateMissilePrefab
{
    public static void Execute()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Missile.glb");
        if (model == null)
        {
            Debug.LogError("Missile model not found!");
            return;
        }

        GameObject root = new GameObject("Missile");
        
        GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        modelInstance.transform.SetParent(root.transform);
        modelInstance.transform.localPosition = Vector3.zero;
        // Adjust rotation if necessary, assuming the model points forward (Z-axis)
        modelInstance.transform.localRotation = Quaternion.Euler(0, 180, 0); // Often GLB models face backwards
        modelInstance.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f); // Adjust scale as needed

        // Add MissileController
        root.AddComponent<MissileController>();

        // Save Prefab
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/MissilePrefab.prefab");
        Object.DestroyImmediate(root);
    }
}
