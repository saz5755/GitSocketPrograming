using UnityEngine;
using UnityEditor;
using System.Text;

public class GroundMoveProbe
{
    public static void Execute()
    {
        var gc = Object.FindFirstObjectByType<GroundController>();
        if (gc == null) { Debug.Log("[Probe] GroundController not found"); return; }

        var go = gc.gameObject;
        var cc = go.GetComponent<CharacterController>();
        var proc = go.GetComponent<ProceduralCharacterAnimator>();

        var sb = new StringBuilder();
        sb.AppendLine($"[Probe] GameObject: {go.name}  Layer: {go.layer} ({LayerMask.LayerToName(go.layer)})");
        sb.AppendLine($"[Probe] CharacterController: radius={cc?.radius} height={cc?.height} center={cc?.center} stepOffset={cc?.stepOffset} slopeLimit={cc?.slopeLimit}");
        sb.AppendLine($"[Probe] Position: {go.transform.position}");
        sb.AppendLine($"[Probe] CurrentSpeed={gc.CurrentSpeed:F3}  CurrentAnimSpeed={gc.CurrentAnimSpeed:F3}  AnimState={gc.CurrentAnimState}");
        sb.AppendLine($"[Probe] ProceduralAnimator present: {proc != null}");

        var cam = Object.FindFirstObjectByType<FlightCamera>();
        if (cam != null)
        {
            sb.AppendLine($"[Probe] FlightCamera found. groundTarget layer check: PlayerMask={LayerMask.GetMask("Player")}");
        }

        // 씬에 있는 모든 레이어 목록
        sb.AppendLine("[Probe] All used layers in scene:");
        foreach (var r in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            string lname = LayerMask.LayerToName(r.layer);
            if (!string.IsNullOrEmpty(lname) && lname != "Default" && lname != "TransparentFX"
                && lname != "Ignore Raycast" && lname != "Water" && lname != "UI")
            {
                // 중복 없이 출력
            }
        }

        Debug.Log(sb.ToString());
    }
}
