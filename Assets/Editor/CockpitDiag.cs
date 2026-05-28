using UnityEngine;
using UnityEditor;

public class CockpitDiag
{
    public static string Execute()
    {
        string prefabPath = "Assets/Prefabs/Player.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return "ERROR: Player.prefab not found";

        var kf21 = prefab.transform.Find("KF21_Model");
        if (kf21 == null) return "ERROR: KF21_Model not found";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== KF21_Model 전체 자식 오브젝트 + Bounds ===");
        sb.AppendLine($"{"Name",-14} {"HasMR",-6} {"Y min":>8} {"Y max":>8} {"Z min":>8} {"Z max":>8}  Shader");

        foreach (Transform child in kf21)
        {
            var mr = child.GetComponent<MeshRenderer>();
            bool hasMR = mr != null;
            string shader = hasMR && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "-";
            // shader 이름 짧게
            if (shader.Length > 28) shader = shader.Substring(shader.Length - 28);

            if (hasMR)
            {
                var b = mr.bounds;
                sb.AppendLine($"{child.name,-14} {hasMR,-6} {b.min.y,8:F3} {b.max.y,8:F3} {b.min.z,8:F3} {b.max.z,8:F3}  {shader}");
            }
            else
            {
                sb.AppendLine($"{child.name,-14} {hasMR,-6} {"N/A",8} {"N/A",8} {"N/A",8} {"N/A",8}  {shader}");
            }
        }
        return sb.ToString();
    }
}
