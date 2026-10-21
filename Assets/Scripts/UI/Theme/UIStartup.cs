using System.Collections;
using UnityEngine;

/// <summary>
/// 씬 시작 직후 몇 프레임은 (특히 에디터에서) 수백 ms 짜리 정지가 끼는데, 이는 프로젝트 코드와 무관한 엔진/에디터 비용이다
/// (카메라만 있는 빈 씬에서도 첫 프레임이 ~225ms). 이 구간에 등장 애니메이션이 걸리면 뚝뚝 끊겨 보이므로,
/// 프레임 시간이 안정될 때까지 애니메이션 시작을 미룬다.
/// </summary>
public static class UIStartup
{
    const float StableDt   = 0.04f;   // 이 이하의 프레임이 연속으로 나오면 안정으로 본다
    const int   StableRun  = 3;
    const float MaxWait    = 2.0f;    // 아무리 불안정해도 이 시간 이상은 기다리지 않는다

    /// <summary>프레임 시간이 안정될 때까지 대기 (코루틴에서 yield return 으로 사용).</summary>
    public static IEnumerator WaitStable()
    {
        float t0 = Time.realtimeSinceStartup;
        int ok = 0;
        while (ok < StableRun && Time.realtimeSinceStartup - t0 < MaxWait)
        {
            yield return null;
            ok = Time.unscaledDeltaTime < StableDt ? ok + 1 : 0;
        }
    }

    /// <summary>UI 전용 씬의 메인 카메라 배경을 UI 기본 배경색(#06111F)으로 — 시작 정지 동안 기본 파란색/회색이 비치지 않게.</summary>
    public static void SetCameraBackground(Camera cam)
    {
        if (cam == null) return;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UITheme.Instance.primaryBG;
    }
}
