using UnityEngine;

// 에스코트 편대 AI 동작 튜닝 데이터.
// Project 우클릭 → Create → KF21 → AI → Escort Behavior 로 에셋 생성 후
// AIManager 또는 EscortAI 컴포넌트의 _behavior 슬롯에 드래그.
[CreateAssetMenu(menuName = "KF21/AI/Escort Behavior", fileName = "EscortBehavior_")]
public class EscortBehaviorSO : ScriptableObject
{
    [Header("편대비행 보간")]
    [Tooltip("슬롯 6m 이내일 때 미세 속도 떨림 폭(±)")]
    [Range(0f, 10f)] public float speedJitterRange = 3f;
    [Tooltip("리더 기준 측면 뱅크각 (좌/우 자동 부호 적용)")]
    public float bankOffsetDeg = 12f;
    [Tooltip("슬롯 z오차가 이 값을 넘으면 감속해서 리더를 통과시킴")]
    public float slotAheadThreshold = 5f;
    [Tooltip("슬롯 거리 이 값 이내에서 보간 가중치 1로 수렴")]
    public float snapDistance = 40f;

    [Header("봇 한계 (Initialize 시 적용)")]
    [Tooltip("에스코트가 추격 시 낼 수 있는 최대 속도")]
    public float maxSpeedOverride = 200f;
    [Tooltip("선회 속도 (deg/s) — 기본 봇 72°/s를 덮어씀")]
    public float turnRateOverride = 150f;

    [Header("캐터펄트 발진")]
    [Tooltip("발진 시 직진 유지 시간")]
    public float launchDuration = 6f;
    [Tooltip("발진 시 직진 목표 속도")]
    public float launchSpeed    = 55f;

    [Header("자유비행 존 경계 회피")]
    [Tooltip("외부 경계 도달 전부터 안쪽으로 조향 시작 거리")]
    public float outerBuffer    = 160f;
    [Tooltip("내부 회피 존 접근 시 바깥쪽으로 조향 시작 거리")]
    public float innerBuffer    = 110f;
    [Tooltip("외부 경계 조향 강도 (0~2 권장)")]
    public float outerSteerGain = 1.8f;
    [Tooltip("내부 회피 존 조향 강도 (0~2 권장)")]
    public float innerSteerGain = 1.6f;
    [Tooltip("EscortZoneTrigger 미할당 시 폴백 반경")]
    public float outerRadiusFallback = 800f;
    [Tooltip("EscortInnerZoneTrigger 미할당 시 폴백 반경")]
    public float innerRadiusFallback = 350f;
    [Tooltip("리더 비행 정지 감지 후 자동 착함 시작까지 대기 시간")]
    public float leaderStopTimeout   = 2f;

    [Header("착함 어프로치")]
    [Tooltip("BeginLanding 호출 시 어프로치 진입 전 자유비행 시간")]
    public float freeFlightDuration = 5f;
    [Tooltip("플레이어 와이어 위치에서 얼마나 뒤에 어프로치 웨이포인트를 설정할지")]
    public float approachDistance = 1500f;
    [Tooltip("어프로치 웨이포인트 고도(와이어 기준 위)")]
    public float approachAltitude = 300f;
    [Tooltip("웨이포인트 도착 판정 거리")]
    public float waypointArriveDistance = 80f;
    [Tooltip("어프로치 평속")]
    public float approachSpeed = 60f;
    [Tooltip("랜딩 직전 최종 속도")]
    public float finalSpeed    = 25f;
    [Tooltip("랜딩 스팟 측면 오프셋 (좌/우 자동 부호 적용)")]
    public float landingSideOffset = 4f;
    [Tooltip("랜딩 스팟 5m 이내에서 와이어 미체결 시 직접 착함 거리")]
    public float directLandDistance = 5f;

    [Header("어레스팅 와이어 체결 감속")]
    [Tooltip("와이어 체결 후 전진 속도 감소율 (m/s²)")]
    public float arrestDecelRate = 80f;
    [Tooltip("체결 후 갑판 Y로 스냅되는 보간 속도 (m/s)")]
    public float arrestYSnapRate = 20f;
    [Tooltip("정지 판정 속도 (이 값 미만이면 Landed 전환)")]
    public float arrestStopSpeed = 0.2f;

    public float GetBankOffset(bool isLeft) => isLeft ? bankOffsetDeg : -bankOffsetDeg;
    public float GetSideOffset(bool isLeft) => isLeft ? -landingSideOffset : landingSideOffset;
}
