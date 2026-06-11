using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameSceneController : MonoBehaviour
{
    Canvas  _msgCanvas;
    Text    _msgText;

    // 에디터 자동 시작: 로그인 결과 수신용 임시 플래그
    bool _autoLoginDone;
    bool _autoLoginOk;

    IEnumerator Start()
    {
#if UNITY_EDITOR
        // ── 에디터에서 GameScene 직접 실행 ──────────────────────────────────
        var dev = DevSettings.Instance;
        if (dev != null && dev.editorAutoStart &&
            (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected()))
        {
            bool ok = false;
            yield return StartCoroutine(EditorAutoStart(dev, result => ok = result));
            if (!ok) yield break;   // 실패 시 EditorAutoStart 내부에서 씬 전환
        }
#endif

        // ── 네트워크 연결 확인 ───────────────────────────────────────────────
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected())
        {
            Debug.LogWarning("[GameScene] Not connected. Redirecting to LoginScene.");
            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
            SceneManager.LoadScene("LoginScene");
            yield break;
        }

        // PlayerManager / ChatUIManager Start() 구독 완료 보장
        yield return null;

        int roomId = (GameManager.Instance != null && GameManager.Instance.currentRoomId > 0)
            ? GameManager.Instance.currentRoomId
            : 0;

        if (roomId <= 0)
        {
            Debug.LogWarning("[GameScene] No room ID. Redirecting to LobbyScene.");
            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
            SceneManager.LoadScene("LobbyScene");
            yield break;
        }

        // ENTER_ROOM_RESULT 구독 (실패 시 로비 복귀)
        var sc = NetworkManager.Instance.socketClient;
        sc.OnEnterRoomResult += HandleEnterRoomResult;

        NetworkManager.Instance.socketClient.EnterRoom(roomId);
        Debug.Log($"[GameScene] Sent ENTER_ROOM → room {roomId}");

        Cursor.visible   = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

#if UNITY_EDITOR
    // ── 에디터 자동 시작: 매니저 생성 → 서버 접속 → 로그인 ─────────────────
    IEnumerator EditorAutoStart(DevSettings dev, System.Action<bool> done)
    {
        // 필수 매니저 자동 생성 (씬에 없을 경우)
        if (GameManager.Instance == null)
            new GameObject("[GameManager]").AddComponent<GameManager>();
        if (NetworkManager.Instance == null)
            new GameObject("[NetworkManager]").AddComponent<NetworkManager>();
        if (UnityMainThreadDispatcher.Instance == null)
            new GameObject("[Dispatcher]").AddComponent<UnityMainThreadDispatcher>();
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        yield return null; // Awake 완료 대기

        // 서버 IP 결정
        string ip = !string.IsNullOrEmpty(dev.autoServerIP)
            ? dev.autoServerIP
            : (GameManager.Instance != null ? GameManager.Instance.serverIP : "127.0.0.1");

        // 서버 접속
        Debug.Log($"[EditorAutoStart] Connecting to {ip}...");
        try
        {
            NetworkManager.Instance.socketClient.Connect(ip);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[EditorAutoStart] Connection failed: {e.Message}");
            SceneManager.LoadScene("LoginScene");
            done(false);
            yield break;
        }

        yield return new WaitForSeconds(0.3f);

        // 로그인
        _autoLoginDone = false;
        _autoLoginOk   = false;
        NetworkManager.Instance.socketClient.OnLoginResult += OnAutoLoginResult;
        NetworkManager.Instance.socketClient.Login(dev.autoNickname, dev.autoPassword);
        Debug.Log($"[EditorAutoStart] Login as '{dev.autoNickname}'...");

        float elapsed = 0f;
        while (!_autoLoginDone && elapsed < 8f)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }
        NetworkManager.Instance.socketClient.OnLoginResult -= OnAutoLoginResult;

        if (!_autoLoginOk)
        {
            Debug.LogError("[EditorAutoStart] Login failed or timed out → LoginScene");
            SceneManager.LoadScene("LoginScene");
            done(false);
            yield break;
        }

        // GameManager에 닉네임·룸 ID 기록 (기존 Start() 흐름이 이어서 사용)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.myNickname    = dev.autoNickname;
            GameManager.Instance.currentRoomId = dev.autoRoomId > 0 ? dev.autoRoomId : 1;
        }

        Debug.Log($"[EditorAutoStart] Success — roomId={GameManager.Instance?.currentRoomId}");
        done(true);
    }

    void OnAutoLoginResult(LoginResultPacket p)
    {
        _autoLoginDone = true;
        _autoLoginOk   = p.success;
        if (!p.success)
            Debug.LogError($"[EditorAutoStart] Login denied: {p.message}");
    }
#endif

    void OnDestroy()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnEnterRoomResult -= HandleEnterRoomResult;
#if UNITY_EDITOR
        if (sc != null) sc.OnLoginResult     -= OnAutoLoginResult;
#endif
    }

    void HandleEnterRoomResult(EnterRoomResultPacket p)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnEnterRoomResult -= HandleEnterRoomResult;

        if (p.success) return;

        Debug.LogWarning($"[GameScene] ENTER_ROOM failed: {p.errorMessage}");
        ShowOverlayMessage(p.errorMessage ?? "Room unavailable");
        StartCoroutine(ReturnToLobby(2.5f));
    }

    void ShowOverlayMessage(string msg)
    {
        if (_msgCanvas != null) return;

        var go    = new GameObject("EnterRoomErrCanvas");
        _msgCanvas = go.AddComponent<Canvas>();
        _msgCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _msgCanvas.sortingOrder = 99;
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        go.AddComponent<GraphicRaycaster>();

        var bg   = new GameObject("BG");
        bg.transform.SetParent(go.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var txtGO = new GameObject("Msg");
        txtGO.transform.SetParent(go.transform, false);
        var tRT   = txtGO.AddComponent<RectTransform>();
        tRT.anchorMin = tRT.anchorMax = new Vector2(0.5f, 0.5f);
        tRT.sizeDelta = new Vector2(700, 80);

        _msgText           = txtGO.AddComponent<Text>();
        _msgText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _msgText.fontSize  = 22;
        _msgText.fontStyle = FontStyle.Bold;
        _msgText.color     = new Color(1f, 0.25f, 0.10f);
        _msgText.alignment = TextAnchor.MiddleCenter;
        _msgText.text      = $"⚠  {msg.ToUpper()}\nReturning to lobby...";

        DontDestroyOnLoad(go);
    }

    IEnumerator ReturnToLobby(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_msgCanvas != null) Destroy(_msgCanvas.gameObject);
        Cursor.visible   = true;
        Cursor.lockState = CursorLockMode.None;
        SceneManager.LoadScene("LobbyScene");
    }
}
