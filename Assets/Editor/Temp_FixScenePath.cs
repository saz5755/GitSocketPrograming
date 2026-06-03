using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

public class Temp_FixScenePath
{
    public static void Execute()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string wrongPath  = "Assets/GameScene.unity";
        string correctPath = "Assets/Scenes/GameScene.unity";

        // 현재 씬을 올바른 경로로 저장
        bool saved = EditorSceneManager.SaveScene(scene, correctPath, false);
        UnityEngine.Debug.Log(saved ? $"[SceneFix] 저장 완료: {correctPath}" : "[SceneFix] 저장 실패");

        // 잘못된 경로 파일 삭제
        if (File.Exists(wrongPath))
        {
            AssetDatabase.DeleteAsset(wrongPath);
            UnityEngine.Debug.Log($"[SceneFix] 삭제: {wrongPath}");
        }

        AssetDatabase.Refresh();
    }
}
