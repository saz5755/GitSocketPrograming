using UnityEngine;

/// <summary>
/// 방 모드 정의 — 방 생성 시 고른 모드에 따라 같은 GameScene 의 AI·UI·무장 구성이 달라진다.
///   AIRSHOW: 에스코트 편대 비행, 무장·킬피드·스코어보드 없음 (플레어는 연출용으로 허용)
///   COMBAT : 적 기체 투입·격추·재투입, 킬피드·SPLASH·스코어보드
/// 에셋: Resources/RoomModes/{AIRSHOW,COMBAT}.asset (Inspector 에서 조정).
/// 모드는 서버 ENTER_ROOM_RESULT.mode 로 받아 SocketClient.roomMode 에 저장되고, 입장 후 바뀌지 않는다.
/// 모드를 모르면(구 서버 등) Current 는 null — 각 시스템은 기존 Inspector 기본값으로 동작한다.
/// </summary>
[CreateAssetMenu(menuName = "KF21/Room Mode", fileName = "RoomMode_")]
public class RoomModeSO : ScriptableObject
{
    public const string Airshow = "AIRSHOW";
    public const string Combat  = "COMBAT";

    [Tooltip("서버 모드 문자열 (AIRSHOW / COMBAT)")]
    public string modeId = Combat;
    public string displayName = "COMBAT";
    [TextArea] public string description;

    [Header("AI")]
    [Tooltip("탑승 가능한 에스코트 편대 (두 모드 모두 필요 — 출격 기체)")]
    public bool spawnEscorts = true;
    [Tooltip("적 기체 투입 + 격추 시 재투입")]
    public bool spawnEnemies = true;

    [Header("무장 / UI")]
    [Tooltip("기총·미사일·락온 사용 (플레어·채프는 항상 허용)")]
    public bool weaponsEnabled = true;
    [Tooltip("킬 피드·SPLASH 콜아웃")]
    public bool showCombatFeed = true;
    [Tooltip("스코어보드 (` 홀드)")]
    public bool showScoreboard = true;

    static RoomModeSO s_current;
    static string     s_currentId;

    /// <summary>현재 방의 모드 (모르면 null)</summary>
    public static RoomModeSO Current
    {
        get
        {
            string id = NetworkManager.Instance?.socketClient?.roomMode;
            if (string.IsNullOrEmpty(id)) return null;
            if (s_current != null && s_currentId == id) return s_current;
            s_currentId = id;
            s_current   = Load(id);
            return s_current;
        }
    }

    public static RoomModeSO Load(string id)
    {
        var so = Resources.Load<RoomModeSO>("RoomModes/" + id);
        if (so != null) return so;
        // 에셋이 없으면 코드 기본값
        so = CreateInstance<RoomModeSO>();
        so.modeId = id;
        if (id == Airshow)
        {
            so.displayName    = "AIRSHOW";
            so.spawnEnemies   = false;
            so.weaponsEnabled = false;
            so.showCombatFeed = false;
            so.showScoreboard = false;
        }
        return so;
    }

    public static bool WeaponsEnabled => Current == null || Current.weaponsEnabled;
    public static bool CombatFeedShown => Current == null || Current.showCombatFeed;
    public static bool ScoreboardShown => Current == null || Current.showScoreboard;
    public static bool IsAirshow => Current != null && Current.modeId == Airshow;
}
