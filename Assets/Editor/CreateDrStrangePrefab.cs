using UnityEditor;
using UnityEngine;

public class CreateDrStrangePrefab
{
    public static void Execute()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/DrStrange_Rigged_idle.glb");
        if (model == null)
        {
            Debug.LogError("DrStrange model not found!");
            return;
        }

        GameObject root = new GameObject("DrStrange_GroundCharacter");
        
        GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        modelInstance.transform.SetParent(root.transform);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.Euler(0, 180, 0); // Face forward

        // Add Animator
        Animator anim = root.AddComponent<Animator>();
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Anim/DrStrange.controller");
        anim.avatar = modelInstance.GetComponent<Animator>().avatar;

        // Add CharacterController
        CharacterController cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        // Save Prefab
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/DrStrange_GroundCharacter.prefab");
        Object.DestroyImmediate(root);
    }
}
