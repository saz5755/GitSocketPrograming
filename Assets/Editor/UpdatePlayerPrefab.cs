using UnityEditor;
using UnityEngine;

public class UpdatePlayerPrefab
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

        // Instantiate the prefab to modify it
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        // Remove PlaneModelBuilder
        PlaneModelBuilder builder = instance.GetComponent<PlaneModelBuilder>();
        if (builder != null)
        {
            Object.DestroyImmediate(builder, true);
        }

        // Remove MeshFilter and MeshRenderer
        MeshFilter mf = instance.GetComponent<MeshFilter>();
        if (mf != null)
        {
            Object.DestroyImmediate(mf, true);
        }
        MeshRenderer mr = instance.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            Object.DestroyImmediate(mr, true);
        }

        // Remove existing children (the parts created by PlaneModelBuilder if any were saved, though they are created at runtime)
        // Wait, PlaneModelBuilder creates parts at runtime in Awake(). So there might not be children in the prefab.
        // Let's check and remove any children just in case.
        for (int i = instance.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(instance.transform.GetChild(i).gameObject, true);
        }

        // Load the new F35 model
        GameObject f35Model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/F35.glb");
        if (f35Model != null)
        {
            GameObject f35Instance = (GameObject)PrefabUtility.InstantiatePrefab(f35Model);
            f35Instance.transform.SetParent(instance.transform);
            f35Instance.transform.localPosition = Vector3.zero;
            f35Instance.transform.localRotation = Quaternion.identity;
            f35Instance.transform.localScale = Vector3.one;
            f35Instance.name = "F35_Model";
        }
        else
        {
            Debug.LogError("F35 model not found at Assets/Models/F35.glb");
        }

        // Save the prefab
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);

        Debug.Log("Player prefab updated successfully.");
    }
}
