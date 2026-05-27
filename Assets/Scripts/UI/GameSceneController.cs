using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameSceneController : MonoBehaviour
{
    Canvas  _msgCanvas;
    Text    _msgText;

    IEnumerator Start()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected())
        {
            Debug.LogWarning("[GameScene] Not connected. Redirecting to LoginScene.");
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

    void OnDestroy()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnEnterRoomResult -= HandleEnterRoomResult;
    }

    void HandleEnterRoomResult(EnterRoomResultPacket p)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnEnterRoomResult -= HandleEnterRoomResult;

        if (p.success) return;

        // 룸이 존재하지 않거나 정원 초과 → 오버레이 메시지 표시 후 로비 복귀
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

        var bg    = new GameObject("BG");
        bg.transform.SetParent(go.transform, false);
        var bgRT  = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.75f);

        var txtGO = new GameObject("Msg");
        txtGO.transform.SetParent(go.transform, false);
        var tRT   = txtGO.AddComponent<RectTransform>();
        tRT.anchorMin = tRT.anchorMax = new Vector2(0.5f, 0.5f);
        tRT.sizeDelta = new Vector2(700, 80);

        _msgText            = txtGO.AddComponent<Text>();
        _msgText.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _msgText.fontSize   = 22;
        _msgText.fontStyle  = FontStyle.Bold;
        _msgText.color      = new Color(1f, 0.25f, 0.10f);
        _msgText.alignment  = TextAnchor.MiddleCenter;
        _msgText.text       = $"⚠  {msg.ToUpper()}\nReturning to lobby...";

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
