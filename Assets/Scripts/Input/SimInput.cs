using UnityEngine;

/// <summary>
/// 시뮬레이터 입력 추상화 — 조작 장치 매핑을 이 한 곳에 모은다.
/// 지금은 키보드/마우스지만, 이후 HOTAS·실기 콕핏 패널(사출 핸들, 스위치 등)을 연결할 때
/// 호출부를 고치지 않고 여기 바인딩만 바꾸면 되도록 "동작" 단위로 노출한다.
/// (비행 축 입력 — 피치/롤/요/스로틀 — 은 v4.1 에서 이곳으로 이전 예정)
/// </summary>
public static class SimInput
{
    // ── 바인딩 ───────────────────────────────────────────────────────────────
    public static KeyCode EjectKey      = KeyCode.E;          // 수정키와 함께 (DCS: LCtrl+E ×3)
    public static KeyCode ScoreboardKey = KeyCode.BackQuote;  // 홀드
    public static KeyCode ConfirmKey    = KeyCode.F;          // 상호작용·재출격

    /// <summary>수정키 (Ctrl) — 단일 키 동작과 조합 동작을 구분한다.</summary>
    public static bool Modifier =>
        Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    /// <summary>사출 핸들 당김 1회. 안전장치(연속 3회)는 기체 쪽(PilotStatus)에서 판정한다.</summary>
    public static bool EjectHandlePulled => Modifier && Input.GetKeyDown(EjectKey);

    public static bool ScoreboardHeld => Input.GetKey(ScoreboardKey);

    public static bool ConfirmPressed => Input.GetKeyDown(ConfirmKey);
}
