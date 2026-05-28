using UnityEditor;
using UnityEngine;

public class CreateSoldierPrefab
{
    public static void Execute()
    {
        const string modelPath      = "Assets/Models/Soldier_Rigged_idle.glb";
        const string controllerPath = "Assets/Anim/Soldier.controller";
        const string prefabPath     = "Assets/Prefabs/Soldier_GroundCharacter.prefab";

        // ── 에셋 로드 ─────────────────────────────────────────────────────
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogError($"[SoldierPrefab] 모델을 찾을 수 없습니다: {modelPath}");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        if (controller == null)
            Debug.LogWarning($"[SoldierPrefab] Animator Controller 없음 — 수동 연결 필요: {controllerPath}");

        // ── 씬에 임시 인스턴스 생성 ────────────────────────────────────────
        var root = (GameObject)Object.Instantiate(model);
        root.name = "Soldier_GroundCharacter";

        // ── Animator Controller 연결 ────────────────────────────────────────
        // GLB 루트 또는 자식에 Animator가 있을 수 있음
        var anim = root.GetComponent<Animator>() ?? root.GetComponentInChildren<Animator>();
        if (anim == null) anim = root.AddComponent<Animator>();
        if (controller != null) anim.runtimeAnimatorController = controller;

        // ── CharacterController ──────────────────────────────────────────────
        if (root.GetComponent<CharacterController>() == null)
        {
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
        }

        // ── GroundController ─────────────────────────────────────────────────
        if (root.GetComponent<GroundController>() == null)
            root.AddComponent<GroundController>();

        // ── 프리팹으로 저장 ───────────────────────────────────────────────────
        var savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
        Object.DestroyImmediate(root);

        if (!success)
        {
            Debug.LogError($"[SoldierPrefab] 프리팹 저장 실패: {prefabPath}");
            return;
        }

        Debug.Log($"[SoldierPrefab] 저장 완료: {prefabPath}");
        AssetDatabase.Refresh();

        // ── PlayerManager.groundCharPrefab 슬롯 자동 연결 ─────────────────────
        var pm = Object.FindObjectOfType<PlayerManager>();
        if (pm != null)
        {
            var so   = new SerializedObject(pm);
            var prop = so.FindProperty("groundCharPrefab");
            if (prop != null)
            {
                prop.objectReferenceValue = savedPrefab;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(pm);
                Debug.Log("[SoldierPrefab] PlayerManager.groundCharPrefab 슬롯 연결 완료");
            }
        }
        else
        {
            Debug.Log("[SoldierPrefab] PlayerManager를 씬에서 찾지 못했습니다 — Inspector에서 수동 연결하세요.");
        }
    }
}
