using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class CheckDrStrangeController
{
    public static void Execute()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Anim/DrStrange.controller");
        if (controller != null)
        {
            foreach (var param in controller.parameters)
            {
                Debug.Log("Param: " + param.name + " type: " + param.type);
            }
            foreach (var state in controller.layers[0].stateMachine.states)
            {
                Debug.Log("State: " + state.state.name);
            }
        }
    }
}
