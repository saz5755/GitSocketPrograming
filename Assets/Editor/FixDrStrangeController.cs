using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class FixDrStrangeController
{
    public static void Execute()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Anim/DrStrange.controller");
        if (controller == null) return;

        // Clear all parameters
        while (controller.parameters.Length > 0)
        {
            controller.RemoveParameter(0);
        }

        // Add correct parameters
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("State", AnimatorControllerParameterType.Int);

        var rootStateMachine = controller.layers[0].stateMachine;

        // Find Locomotion state
        AnimatorState locomotionState = null;
        foreach (var state in rootStateMachine.states)
        {
            if (state.state.name == "Locomotion")
            {
                locomotionState = state.state;
                break;
            }
        }

        // Add Jump state
        AnimatorState jumpState = null;
        foreach (var state in rootStateMachine.states)
        {
            if (state.state.name == "Jump")
            {
                jumpState = state.state;
                break;
            }
        }

        if (jumpState == null)
        {
            jumpState = rootStateMachine.AddState("Jump");
            var jumpClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Models/DrStrange_Rigged_jump.glb");
            // We need to find the actual clip inside the GLB
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Models/DrStrange_Rigged_jump.glb");
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && clip.name.Contains("Basic_Jump"))
                {
                    jumpState.motion = clip;
                    break;
                }
            }
        }

        // Clear existing transitions from AnyState
        var anyStateTransitions = rootStateMachine.anyStateTransitions;
        foreach (var t in anyStateTransitions)
        {
            rootStateMachine.RemoveAnyStateTransition(t);
        }

        // Add AnyState -> Jump transition
        var jumpTransition = rootStateMachine.AddAnyStateTransition(jumpState);
        jumpTransition.hasExitTime = false;
        jumpTransition.duration = 0.1f;
        jumpTransition.AddCondition(AnimatorConditionMode.Equals, 3, "State");

        // Clear existing transitions from Jump
        var jumpTransitions = jumpState.transitions;
        foreach (var t in jumpTransitions)
        {
            jumpState.RemoveTransition(t);
        }

        // Add Jump -> Locomotion transition
        if (locomotionState != null)
        {
            var backTransition = jumpState.AddTransition(locomotionState);
            backTransition.hasExitTime = false;
            backTransition.duration = 0.2f;
            backTransition.AddCondition(AnimatorConditionMode.NotEqual, 3, "State");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("DrStrange controller fixed and Jump state added.");
    }
}
