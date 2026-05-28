using UnityEditor;
using UnityEngine;

public class FixDrStrangePrefab
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

        // Remove Animator from root
        Animator rootAnim = instance.GetComponent<Animator>();
        if (rootAnim != null)
        {
            Object.DestroyImmediate(rootAnim);
        }

        // Find the model instance (child)
        Transform modelTransform = instance.transform.GetChild(0);
        if (modelTransform != null)
        {
            // Ensure it faces forward. Meshy GLB models usually face +Z when rotation is (0, 180, 0) or (0, 0, 0).
            // Let's set it to (0, 180, 0) first. If it's backwards, we can change it.
            modelTransform.localRotation = Quaternion.Euler(0, 180, 0);

            Animator childAnim = modelTransform.GetComponent<Animator>();
            if (childAnim == null)
            {
                childAnim = modelTransform.gameObject.AddComponent<Animator>();
            }
            
            childAnim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Anim/DrStrange.controller");
            
            // The avatar should be automatically set if it's an imported model, but let's ensure it's there.
            // If not, we can't easily set it via script without loading the original asset.
            // Since it's a prefab instance of the GLB, it should have the avatar.
        }

        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
        Debug.Log("Fixed DrStrange prefab Animator hierarchy.");
    }
}
