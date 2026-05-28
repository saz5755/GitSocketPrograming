using UnityEngine;
using UnityEditor;

public class CockpitPositionAnalyzer
{
    public static string Execute()
    {
        string prefabPath = "Assets/Prefabs/Player.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return "ERROR: Player.prefab not found";

        var kf21 = prefab.transform.Find("KF21_Model");
        if (kf21 == null) return "ERROR: KF21_Model not found";

        var sb = new System.Text.StringBuilder();

        // KF21_Model 자체 정보
        sb.AppendLine($"=== KF21_Model ===");
        sb.AppendLine($"  localPos: {kf21.localPosition}");
        sb.AppendLine($"  scale:    {kf21.localScale}");

        // 조종석 관련 오브젝트 위치 수집
        string[] targets = { "Object_6", "Object_10", "Object_12", "Object_13",
                             "Object_14", "Object_15", "Object_16", "Object_20", "Object_21" };

        sb.AppendLine($"\n=== Cockpit Objects (Player-local space) ===");

        Vector3 interiorSum = Vector3.zero;
        int interiorCount = 0;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (string name in targets)
        {
            var obj = FindDeep(kf21, name);
            if (obj == null) { sb.AppendLine($"  {name}: NOT FOUND"); continue; }

            // Player-local 좌표로 변환 (prefab root 기준)
            Vector3 worldPos = obj.position; // prefab에서는 localToWorld = identity
            sb.AppendLine($"  {name}: pos={obj.position:F3}  (local={obj.localPosition:F3})");

            if (name != "Object_6")
            {
                // MeshRenderer bounds center 구하기
                var mr = obj.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Vector3 c = mr.bounds.center;
                    sb.AppendLine($"         bounds.center={c:F3}");
                    interiorSum += c;
                    interiorCount++;
                    minY = Mathf.Min(minY, mr.bounds.min.y);
                    maxY = Mathf.Max(maxY, mr.bounds.max.y);
                    minZ = Mathf.Min(minZ, mr.bounds.min.z);
                    maxZ = Mathf.Max(maxZ, mr.bounds.max.z);
                }
            }
        }

        if (interiorCount > 0)
        {
            Vector3 center = interiorSum / interiorCount;
            sb.AppendLine($"\n=== Interior Bounds Summary ===");
            sb.AppendLine($"  Average center: {center:F3}");
            sb.AppendLine($"  Y range: {minY:F3} ~ {maxY:F3}  (mid={((minY+maxY)/2f):F3})");
            sb.AppendLine($"  Z range: {minZ:F3} ~ {maxZ:F3}  (mid={((minZ+maxZ)/2f):F3})");
            sb.AppendLine($"\n=== Suggested cockpitOffset (Player-local) ===");
            // 눈 위치는 Y 상단 70% 지점, Z는 중간
            float eyeY = minY + (maxY - minY) * 0.70f;
            float eyeZ = (minZ + maxZ) * 0.5f;
            sb.AppendLine($"  cockpitOffset = (0, {eyeY:F2}, {eyeZ:F2})");
        }

        return sb.ToString();
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
