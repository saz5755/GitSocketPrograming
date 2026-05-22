using UnityEditor;
using UnityEngine;

public class CaptureF35Rotated
{
    public static void Execute()
    {
        GameObject oldTest = GameObject.Find("Player_F35_Test");
        if (oldTest != null)
        {
            Object.DestroyImmediate(oldTest);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        if (prefab != null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "Player_F35_Test";
            instance.transform.position = Vector3.zero;
            
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
            Selection.activeGameObject = instance;
        }
    }
}
