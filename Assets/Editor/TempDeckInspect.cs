using UnityEngine;
using UnityEditor;

public class TempDeckInspect
{
    public static void Execute()
    {
        var carrier = GameObject.Find("항공모함");
        if (carrier == null) { Debug.LogError("[TempDeck] 항공모함 not found"); return; }

        // 모든 자식 Renderer bounds 수집
        var renderers = carrier.GetComponentsInChildren<Renderer>();
        float globalMaxY = float.MinValue;

        // Wire Z 범위(35~60) 근처 렌더러 중 가장 높은 Y 찾기
        float wireAreaMaxY = float.MinValue;
        foreach (var r in renderers)
        {
            if (r.bounds.max.y > globalMaxY) globalMaxY = r.bounds.max.y;

            Vector3 lc = carrier.transform.InverseTransformPoint(r.bounds.center);
            if (lc.z >= 35f && lc.z <= 60f)
            {
                if (r.bounds.max.y > wireAreaMaxY) wireAreaMaxY = r.bounds.max.y;
                Debug.Log($"[TempDeck] Wire-area renderer: {r.name}  localZ={lc.z:F1}  boundsMaxY={r.bounds.max.y:F3}  boundsMinY={r.bounds.min.y:F3}");
            }
        }

        Debug.Log($"[TempDeck] Global renderer maxY = {globalMaxY:F3}");
        Debug.Log($"[TempDeck] Wire-area renderer maxY = {wireAreaMaxY:F3}");

        // 전체 Renderer 중 Y가 가장 평탄한(두께 얇은) 그룹 → 갑판 후보
        Debug.Log($"[TempDeck] Carrier world pos = {carrier.transform.position}");
        Debug.Log($"[TempDeck] Carrier bounds center = {new Renderer[0].Length}");

        // BoxCollider 정보
        var bc = carrier.GetComponent<BoxCollider>();
        if (bc != null)
            Debug.Log($"[TempDeck] Root BoxCollider: center={bc.center}  size={bc.size}  isTrigger={bc.isTrigger}");
    }
}
