using UnityEngine;

/// <summary>
/// 씬 하이어라키에 배치해두는 개발자 디버그 설정.
/// Managers 오브젝트에 컴포넌트로 추가.
/// </summary>
public class DevSettings : MonoBehaviour
{
    public static DevSettings Instance { get; private set; }

    [Header("Preflight")]
    [Tooltip("true 시 APU/ENG/AV 절차 생략, 탑승 직후 바로 F키 이륙 가능")]
    public bool skipPreflight = false;

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
