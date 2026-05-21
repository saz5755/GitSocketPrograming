using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;

public static class SceneSetupTool
{
    [MenuItem("Tools/Setup All Scenes (Login + Lobby + Game)")]
    public static void SetupAllScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        SetupLoginScene();
        SetupLobbyScene();
        SetupGameScene();
        SetupBuildSettings();

        EditorSceneManager.OpenScene("Assets/Scenes/LoginScene.unity");
        Debug.Log("[SceneSetup] 완료! LoginScene > LobbyScene > GameScene 모두 설정됐습니다.");
        EditorUtility.DisplayDialog("씬 설정 완료",
            "LoginScene / LobbyScene / GameScene 설정이 완료됐습니다.\n\n" +
            "Play 버튼을 눌러 LoginScene 부터 테스트하세요.\n" +
            "(Build Settings: 0=Login, 1=Lobby, 2=Game)",
            "확인");
    }

    // ── LoginScene ─────────────────────────────────────────────────────────
    static void SetupLoginScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라 (UI 씬에도 필수)
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.031f, 0.043f, 0.063f);
        cam.orthographic     = false;
        camGO.AddComponent<AudioListener>();

        // LoginUI
        new GameObject("LoginUI").AddComponent<LoginUI>();

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/LoginScene.unity");
        Debug.Log("[SceneSetup] LoginScene 저장");
    }

    // ── LobbyScene ─────────────────────────────────────────────────────────
    static void SetupLobbyScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.031f, 0.043f, 0.063f);
        camGO.AddComponent<AudioListener>();

        // LobbyUI
        new GameObject("LobbyUI").AddComponent<LobbyUI>();

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/LobbyScene.unity");
        Debug.Log("[SceneSetup] LobbyScene 저장");
    }

    // ── GameScene ──────────────────────────────────────────────────────────
    static void SetupGameScene()
    {
        bool hasSample = File.Exists("Assets/Scenes/SampleScene.unity");
        var scene = hasSample
            ? EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // GameSceneController
        if (FindType<GameSceneController>() == null)
            new GameObject("GameController").AddComponent<GameSceneController>();

        // InGameMenuUI
        if (FindType<InGameMenuUI>() == null)
            new GameObject("PauseMenu").AddComponent<InGameMenuUI>();

        // FlightHUD (GameScene 전용)
        if (FindType<FlightHUD>() == null)
            new GameObject("FlightHUD").AddComponent<FlightHUD>();

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/GameScene.unity");
        Debug.Log("[SceneSetup] GameScene 저장");
    }

    // ── Build Settings ─────────────────────────────────────────────────────
    static void SetupBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/LoginScene.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/LobbyScene.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/GameScene.unity",  true),
        };
        Debug.Log("[SceneSetup] Build Settings: 0=Login 1=Lobby 2=Game");
    }

    static T FindType<T>() where T : Object
        => Object.FindObjectOfType<T>();
}
