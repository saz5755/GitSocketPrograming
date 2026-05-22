using UnityEditor;
using UnityEngine;

public class AttachF35VFX
{
    public static void Execute()
    {
        string prefabPath = "Assets/Prefabs/Player.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError("Prefab not found at " + prefabPath);
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        
        if (instance.GetComponent<F35VFXController>() == null)
        {
            instance.AddComponent<F35VFXController>();
            Debug.Log("Added F35VFXController to Player prefab.");
        }

        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
    }
}
