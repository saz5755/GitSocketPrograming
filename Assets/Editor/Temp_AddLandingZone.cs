using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class Temp_AddLandingZone
{
    public static void Execute()
    {
        var carrier = GameObject.Find("항공모함");
        if (carrier == null) { Debug.LogError("[LandingZone] 항공모함 오브젝트를 찾을 수 없음"); return; }

        // 기존 LandingZone 있으면 제거
        var existing = carrier.transform.Find("LandingZone");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        var go = new GameObject("LandingZone");
        go.transform.SetParent(carrier.transform, false);
        // 항모 갑판 위, 착함 구역(후방 중앙)에 배치
        go.transform.localPosition = new Vector3(0f, 1f, -30f);
        go.transform.localRotation = Quaternion.identity;

        var zone = go.AddComponent<AircraftZone>();

        // SerializedObject로 직렬화 필드 설정
        var so = new SerializedObject(zone);
        so.FindProperty("_zoneType").enumValueIndex = (int)AircraftZone.Type.Landing;
        so.FindProperty("_radius").floatValue = 35f;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkAllScenesDirty();
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"[LandingZone] 생성 완료: {go.transform.position} (로컬: {go.transform.localPosition}), radius=35");
    }
}
