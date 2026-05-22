using UnityEditor;
using UnityEngine;

public class CaptureF35
{
    public static void Execute()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        if (prefab != null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "Player_F35_Test";
            instance.transform.position = Vector3.zero;
            
            // Focus the scene view on the object
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
            Selection.activeGameObject = instance;
        }
    }
}
