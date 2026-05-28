using UnityEditor;
using UnityEngine;

public class InspectKF21Model
{
    public static void Execute()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        if (prefab != null)
        {
            Transform kf21 = prefab.transform.Find("KF21_Model");
            if (kf21 != null)
            {
                Debug.Log("Found KF21_Model");
                Renderer[] renderers = kf21.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    Debug.Log("Renderer: " + r.name + ", Material: " + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
                }
            }
            else
            {
                Debug.Log("KF21_Model not found in Player prefab");
            }
        }
    }
}
