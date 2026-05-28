using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class AssignDrStrangeToGameScene
{
    public static void Execute()
    {
        string scenePath = "Assets/Scenes/GameScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        
        PlayerManager pm = Object.FindObjectOfType<PlayerManager>();
        if (pm != null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DrStrange_GroundCharacter.prefab");
            if (prefab != null)
            {
                SerializedObject so = new SerializedObject(pm);
                so.Update();
                so.FindProperty("groundCharPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
                
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Successfully assigned DrStrange prefab to PlayerManager in GameScene.");
            }
            else
            {
                Debug.LogError("DrStrange prefab not found!");
            }
        }
        else
        {
            Debug.LogError("PlayerManager not found in GameScene!");
        }
    }
}
