using UnityEngine;

/// <summary>
/// 씬 하이어라키에 배치해두는 개발자 디버그 설정.
/// [Managers] 오브젝트에 컴포넌트로 추가.
/// </summary>
public class DevSettings : MonoBehaviour
{
    public static DevSettings Instance { get; private set; }

    [Header("Preflight")]
    [Tooltip("true 시 APU/ENG/AV 절차 생략, 탑승 직후 바로 E키 이륙 가능")]
    public bool skipPreflight = false;

    [Header("에디터 GameScene 직접 실행 (개발 전용 — 빌드에는 영향 없음)")]
    [Tooltip("GameScene을 에디터에서 직접 Play 할 때 자동으로 서버 접속·로그인·룸 입장")]
    public bool editorAutoStart = false;

    [Tooltip("자동 접속 서버 IP (비워두면 GameManager.serverIP 사용)")]
    public string autoServerIP = "";

    [Tooltip("자동 로그인 닉네임(ID)")]
    public string autoNickname = "Editor";

    [Tooltip("자동 로그인 패스워드")]
    public string autoPassword = "1234";

    [Tooltip("자동 입장할 룸 ID (서버에 해당 룸이 없으면 로비로 이동)")]
    public int autoRoomId = 1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
