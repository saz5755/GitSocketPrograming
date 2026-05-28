using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class FixCockpitOffset
{
    public static string Execute()
    {
        var sb = new System.Text.StringBuilder();
        int totalFixed = 0;

        string[] scenes = {
            "Assets/Scenes/GameScene.unity",
            "Assets/Scenes/SampleScene.unity",
        };

        foreach (string scenePath in scenes)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            int fixedInScene = 0;

            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var fc in go.GetComponentsInChildren<FlightCamera>(true))
                {
                    var so = new SerializedObject(fc);

                    var offset = so.FindProperty("cockpitOffset");
                    if (offset != null)
                    {
                        Vector3 old = offset.vector3Value;
                        offset.vector3Value = new Vector3(0f, 0.52f, 2.45f);
                        sb.AppendLine($"  {scene.name}/{fc.gameObject.name}: offset {old:F2} → (0.00, 0.52, 2.45)");
                    }

                    var tilt = so.FindProperty("cockpitDownTilt");
                    if (tilt != null)
                    {
                        float old = tilt.floatValue;
                        tilt.floatValue = 8f;
                        sb.AppendLine($"  {scene.name}/{fc.gameObject.name}: downTilt {old:F1} → 8.0");
                    }

                    so.ApplyModifiedProperties();
                    fixedInScene++;
                    totalFixed++;
                }
            }

            if (fixedInScene > 0)
                EditorSceneManager.SaveScene(scene);

            EditorSceneManager.CloseScene(scene, true);
        }

        sb.Insert(0, $"Fixed {totalFixed} FlightCamera(s)\n");
        return sb.ToString();
    }
}
