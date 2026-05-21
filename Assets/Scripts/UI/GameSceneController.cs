using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// GameScene 의 빈 GameObject 에 붙입니다.
/// 씬 로드 후 한 프레임 대기해서 PlayerManager.Start() 이벤트 구독이 완료된 뒤
/// 서버에 ENTER_ROOM 을 전송합니다.
/// </summary>
public class GameSceneController : MonoBehaviour
{
    IEnumerator Start()
    {
        // 연결 상태 확인 — 개발 중 직접 GameScene 실행 시 LoginScene 으로 리다이렉트
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected())
        {
            Debug.LogWarning("[GameScene] Not connected. Redirecting to LoginScene.");
            SceneManager.LoadScene("LoginScene");
            yield break;
        }

        // 한 프레임 대기: PlayerManager / ChatUIManager 의 Start() 구독 완료 보장
        yield return null;

        int roomId = (GameManager.Instance != null && GameManager.Instance.currentRoomId > 0)
            ? GameManager.Instance.currentRoomId
            : 1;

        NetworkManager.Instance.socketClient.EnterRoom(roomId);
        Debug.Log($"[GameScene] Entered room {roomId}");

        // 커서 잠금 (1인칭 비행 모드)
        Cursor.visible   = false;
        Cursor.lockState = CursorLockMode.Locked;
    }
}
