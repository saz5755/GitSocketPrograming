using System.Diagnostics;

/// <summary>
/// 개발용 상태 로그 — 에디터에서만 남고 빌드에서는 호출 자체가 제거된다 (문자열 보간 할당도 함께 사라짐).
/// 경고·오류는 그대로 Debug.LogWarning/LogError 를 쓴다.
/// </summary>
public static class DevLog
{
    [Conditional("UNITY_EDITOR")]
    public static void Log(string message) => UnityEngine.Debug.Log(message);
}
