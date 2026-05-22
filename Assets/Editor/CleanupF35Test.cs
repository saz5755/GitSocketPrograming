using UnityEditor;
using UnityEngine;

public class CleanupF35Test
{
    public static void Execute()
    {
        GameObject testObj = GameObject.Find("Player_F35_Test");
        if (testObj != null)
        {
            Object.DestroyImmediate(testObj);
        }
    }
}
