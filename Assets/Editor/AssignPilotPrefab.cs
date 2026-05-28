using UnityEditor;
using UnityEngine;

public class AssignPilotPrefab
{
    public static void Execute()
    {
        PlayerManager pm = Object.FindObjectOfType<PlayerManager>();
        if (pm != null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Pilot_GroundCharacter.prefab");
            if (prefab != null)
            {
                SerializedObject so = new SerializedObject(pm);
                so.Update();
                so.FindProperty("groundCharPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
                Debug.Log("Assigned Pilot_GroundCharacter prefab to PlayerManager.");
            }
            else
            {
                Debug.LogError("Pilot_GroundCharacter prefab not found.");
            }
        }
        else
        {
            Debug.LogError("PlayerManager not found in scene.");
        }
    }
}
