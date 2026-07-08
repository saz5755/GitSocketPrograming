using UnityEngine;
using UnityEditor;

public class AssignWireMaterials
{
    public static void Execute()
    {
        const string soPath   = "Assets/ScriptableObject/WireSystem.asset";
        const string matDir   = "Assets/Materials/WireSystem";

        // 폴더 생성
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(matDir))
            AssetDatabase.CreateFolder("Assets/Materials", "WireSystem");

        var so = AssetDatabase.LoadAssetAtPath<WireSystemSO>(soPath);
        if (so == null) { Debug.LogError("[AssignWireMaterials] WireSystem.asset 로드 실패: " + soPath); return; }

        // ── 1. wireRopeMaterial (V자 LineRenderer) ─────────────────────────────
        if (so.wireRopeMaterial == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");
            if (sh == null) { Debug.LogError("[AssignWireMaterials] URP Unlit 셰이더를 찾을 수 없습니다."); }
            else
            {
                var mat = new Material(sh);
                mat.name = "WireRope";
                mat.color = new Color(0.90f, 0.80f, 0.55f, 1f); // lineColorAvailable
                AssetDatabase.CreateAsset(mat, matDir + "/WireRope.mat");
                so.wireRopeMaterial = mat;
                Debug.Log("[AssignWireMaterials] WireRope.mat 생성 완료");
            }
        }
        else Debug.Log("[AssignWireMaterials] wireRopeMaterial 이미 할당됨 — 스킵");

        // ── 2. zoneDiscMaterial (투명 존 디스크) ────────────────────────────────
        if (so.zoneDiscMaterial == null)
        {
            // URP Transparent
            Shader shT = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Standard");
            if (shT == null) { Debug.LogError("[AssignWireMaterials] Transparent 셰이더를 찾을 수 없습니다."); }
            else
            {
                var mat = new Material(shT);
                mat.name = "WireZoneDisc";
                // URP Transparent 설정
                mat.SetFloat("_Surface", 1f);           // Surface Type = Transparent
                mat.SetFloat("_Blend", 0f);             // Alpha blending
                mat.SetFloat("_AlphaClip", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.color = new Color(0.40f, 0.00f, 0.80f, 0.35f); // 보라 반투명
                AssetDatabase.CreateAsset(mat, matDir + "/WireZoneDisc.mat");
                so.zoneDiscMaterial = mat;
                Debug.Log("[AssignWireMaterials] WireZoneDisc.mat 생성 완료");
            }
        }
        else Debug.Log("[AssignWireMaterials] zoneDiscMaterial 이미 할당됨 — 스킵");

        // ── 3. wireSteelMaterial (볼라드/케이블 본체) ───────────────────────────
        // 비워두면 런타임에 갑판 머티리얼 복사로 대응 → 빌드에서도 안전, 스킵
        // 필요 시 아래 주석 해제
        /*
        if (so.wireSteelMaterial == null)
        {
            Shader shS = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shS != null)
            {
                var mat = new Material(shS);
                mat.name = "WireSteel";
                mat.color = new Color(0.55f, 0.55f, 0.60f, 1f);
                AssetDatabase.CreateAsset(mat, matDir + "/WireSteel.mat");
                so.wireSteelMaterial = mat;
            }
        }
        */

        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AssignWireMaterials] WireSystem.asset 저장 완료");
    }
}
