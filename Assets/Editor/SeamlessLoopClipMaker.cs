using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 끝 프레임이 첫 프레임과 맞지 않는 루프 클립(Meshy glb 의 Idle 등)을 이음매 없는 사본으로 만든다.
/// Generic 리그라 Loop Pose 보정이 없어, 4초마다 골반 위치·다리 회전이 첫 프레임으로 튀며 발이 미끄러져 보였다.
/// 마지막 blendSec 동안 각 커브에 (첫 값 - 끝 값) 을 smoothstep 으로 더해 끝이 첫 프레임에 정확히 닿게 한다.
/// 사본은 Assets/Anim/Loops 에 저장하고, Assets/Anim 의 컨트롤러가 원본을 쓰던 자리(블렌드 트리 포함)를 사본으로 바꾼다.
/// glb 를 다시 받으면 메뉴에서 다시 실행한다.
/// </summary>
public static class SeamlessLoopClipMaker
{
    const string OutDir = "Assets/Anim/Loops";
    const float BlendSec = 0.5f;

    static readonly string[] Sources =
    {
        "Assets/Models/DrStrange_Rigged_idle.glb",
        "Assets/Models/Soldier_Rigged_idle.glb",
        "Assets/Models/Pilot_Rigged_idle.glb",
    };

    [MenuItem("Tools/Animation/Make Seamless Idle Loops")]
    public static void MakeMenu() => Debug.Log(Make());

    public static string Make()
    {
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Anim", "Loops");
        var log = new System.Text.StringBuilder();
        var swap = new Dictionary<Motion, Motion>();
        foreach (var src in Sources)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(src).OfType<AnimationClip>().FirstOrDefault();
            if (clip == null) { log.AppendLine($"건너뜀 (클립 없음): {src}"); continue; }
            string dst = $"{OutDir}/{Path.GetFileNameWithoutExtension(src)}_loop.anim";
            var loop = BuildSeamless(clip, out float maxGap);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
            if (existing != null) { EditorUtility.CopySerialized(loop, existing); loop = existing; }
            else AssetDatabase.CreateAsset(loop, dst);
            swap[clip] = loop;
            log.AppendLine($"{src} → {dst} (최대 이음매 차 {maxGap:F3} 보정)");
        }
        AssetDatabase.SaveAssets();

        foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Anim" }))
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            int n = 0;
            foreach (var layer in ctrl.layers) n += SwapInMachine(layer.stateMachine, swap);
            if (n > 0) { EditorUtility.SetDirty(ctrl); log.AppendLine($"{ctrl.name}.controller: {n}곳 교체"); }
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static AnimationClip BuildSeamless(AnimationClip src, out float maxGap)
    {
        maxGap = 0f;
        var dst = new AnimationClip { name = src.name + "_loop", frameRate = src.frameRate };
        float len = src.length, start = len - Mathf.Min(BlendSec, len * 0.5f);
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            var c = AnimationUtility.GetEditorCurve(src, b);
            if (c == null || c.length < 2) { AnimationUtility.SetEditorCurve(dst, b, c); continue; }
            float gap = c.keys[0].value - c.keys[c.length - 1].value;
            if (!b.propertyName.Contains("Rotation")) maxGap = Mathf.Max(maxGap, Mathf.Abs(gap));
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].time <= start) continue;
                float w = Mathf.SmoothStep(0f, 1f, (keys[i].time - start) / (len - start));
                keys[i].value += gap * w;
            }
            keys[keys.Length - 1].value = keys[0].value;
            var nc = new AnimationCurve(keys);
            for (int i = 0; i < nc.length; i++) AnimationUtility.SetKeyLeftTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto);
            for (int i = 0; i < nc.length; i++) AnimationUtility.SetKeyRightTangentMode(nc, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetEditorCurve(dst, b, nc);
        }
        var s = AnimationUtility.GetAnimationClipSettings(src);
        s.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(dst, s);
        return dst;
    }

    static int SwapInMachine(AnimatorStateMachine sm, Dictionary<Motion, Motion> swap)
    {
        int n = 0;
        foreach (var cs in sm.states)
        {
            var st = cs.state;
            if (st.motion != null && swap.TryGetValue(st.motion, out var rep)) { st.motion = rep; n++; }
            else if (st.motion is BlendTree bt) n += SwapInTree(bt, swap);
        }
        foreach (var sub in sm.stateMachines) n += SwapInMachine(sub.stateMachine, swap);
        return n;
    }

    static int SwapInTree(BlendTree bt, Dictionary<Motion, Motion> swap)
    {
        int n = 0;
        var children = bt.children;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].motion != null && swap.TryGetValue(children[i].motion, out var rep)) { children[i].motion = rep; n++; }
            else if (children[i].motion is BlendTree sub) n += SwapInTree(sub, swap);
        }
        if (n > 0) { bt.children = children; EditorUtility.SetDirty(bt); }
        return n;
    }
}
