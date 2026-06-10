using UnityEngine;

// AI 봇 컨트롤러 기본 동작 데이터.
// Project 우클릭 → Create → KF21 → AI → Bot Config 로 에셋 생성 후
// AIBotController._config 슬롯에 드래그.
//
// Initialize 시점에 인스턴스 변수로 복사되므로, 런타임에 SetMaxSpeed/SetTurnRate로 덮어쓸 수 있다.
[CreateAssetMenu(menuName = "KF21/AI/Bot Config", fileName = "AIBotConfig_")]
public class AIBotConfigSO : ScriptableObject
{
    [Header("비행 성능 (Initialize 시 초기값)")]
    [Tooltip("최대 속도 (m/s). 플레이어 기본 80m/s 기준 70% = 56")]
    public float maxSpeed = 56f;
    [Tooltip("가속 (m/s²)")]
    public float accel = 22f;
    [Tooltip("감속 (m/s²)")]
    public float decel = 30f;
    [Tooltip("선회 속도 (deg/s). EnemyAI는 낮게, EscortAI는 높게 덮어씀")]
    public float turnRate = 72f;

    [Header("네트워크")]
    [Tooltip("UDP 위치 브로드캐스트 주기. 낮을수록 부드럽지만 대역폭이 늘어남")]
    [Range(0.01f, 0.5f)]
    public float netUpdateInterval = 0.05f;
    [Tooltip("정지 상태에서 브로드캐스트 생략 (불필요한 패킷 절약)")]
    public bool skipBroadcastWhenStationary = true;
    [Tooltip("정지 판정 속도 (이 값 미만이면 정지로 간주)")]
    public float stationarySpeedThreshold = 0.1f;
}
