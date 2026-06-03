using UnityEditor;

public class Temp_FixCountermeasureKeys
{
    public static void Execute()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/Player.prefab");
        if (prefab == null) { UnityEngine.Debug.LogError("[Fix] Player.prefab 없음"); return; }

        var cms = prefab.GetComponentInChildren<CountermeasureSystem>();
        if (cms == null) { UnityEngine.Debug.LogError("[Fix] CountermeasureSystem 없음"); return; }

        var so = new SerializedObject(cms);
        so.FindProperty("flareKey").intValue = (int)UnityEngine.KeyCode.X;
        so.FindProperty("chaffKey").intValue = (int)UnityEngine.KeyCode.Z;
        so.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("[Fix] CountermeasureSystem keys → Flare:X / Chaff:Z");
    }
}
