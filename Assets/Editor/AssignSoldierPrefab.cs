using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class AssignSoldierPrefab
{
    public static void Execute()
    {
        const string scenePath  = "Assets/Scenes/GameScene.unity";
        const string prefabPath = "Assets/Prefabs/Soldier_GroundCharacter.prefab";

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Assign] 프리팹을 찾을 수 없습니다: {prefabPath}");
            return;
        }

        // 현재 씬 저장 후 GameScene을 Additive로 열기
        var gameScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        PlayerManager pm = null;
        foreach (var go in gameScene.GetRootGameObjects())
        {
            pm = go.GetComponentInChildren<PlayerManager>(true);
            if (pm != null) break;
        }

        if (pm == null)
        {
            Debug.LogError("[Assign] GameScene에서 PlayerManager를 찾지 못했습니다.");
            EditorSceneManager.CloseScene(gameScene, true);
            return;
        }

        var so   = new SerializedObject(pm);
        var prop = so.FindProperty("groundCharPrefab");
        prop.objectReferenceValue = prefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(pm);

        EditorSceneManager.SaveScene(gameScene);
        EditorSceneManager.CloseScene(gameScene, true);

        Debug.Log("[Assign] PlayerManager.groundCharPrefab → Soldier_GroundCharacter 연결 및 저장 완료");
    }
}
