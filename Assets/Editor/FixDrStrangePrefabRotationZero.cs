using UnityEditor;
using UnityEngine;

public class FixDrStrangePrefabRotationZero
{
    public static void Execute()
    {
        string prefabPath = "Assets/Prefabs/DrStrange_GroundCharacter.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("Prefab not found!");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        // Find the model instance (child)
        Transform modelTransform = instance.transform.GetChild(0);
        if (modelTransform != null)
        {
            // Set Y rotation to 0 degrees as requested
            modelTransform.localRotation = Quaternion.Euler(0, 0, 0);
        }

        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
        Debug.Log("Fixed DrStrange prefab rotation to Y=0.");
    }
}
