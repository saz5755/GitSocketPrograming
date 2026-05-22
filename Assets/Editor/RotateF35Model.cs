using UnityEditor;
using UnityEngine;

public class RotateF35Model
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
        
        Transform f35Model = instance.transform.Find("F35_Model");
        if (f35Model != null)
        {
            // Rotate by 180 degrees on Y axis
            f35Model.localRotation = Quaternion.Euler(0, 180, 0);
            Debug.Log("Rotated F35_Model by 180 degrees on Y axis.");
        }
        else
        {
            Debug.LogError("F35_Model not found in Player prefab.");
        }

        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
    }
}
