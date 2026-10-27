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
    [Tooltip("리드 포인트 거리 = 리더 속도 × 이 값 (초). 클수록 횡·수직 오차를 느긋하게 메운다 (최소 80m)")]
    public float formationLeadTime = 1.5f;
    [Tooltip("슬롯 앞뒤 오차 1m 당 속도 보정량 (m/s)")]
    public float formationSpeedGain = 0.6f;
    [Tooltip("편대 기준 좌표계를 수평으로 되돌리는 속도 (deg/s). 리더 롤은 따라가지 않고(제자리 롤), " +
             "루프 등으로 기준이 기울어지면 이 속도로 다시 수평 편대를 갖춘다")]
    public float formationRelevelRate = 45f;
    [Tooltip("편대비행 중 리더 롤 추종 최대 속도 (deg/s). 리더 배럴롤(240°/s)보다 커야 연속 롤에서도 뒤처지지 않는다")]
    public float formationRollRate = 300f;
    [Tooltip("롤 웨이브: 편대 한 열(리더 뒤 40m) 당 롤 시작 지연 (초). 0 이면 전 기체 동시 롤")]
    [Range(0f, 0.5f)] public float rollWaveDelay = 0.1f;
    [Tooltip("편대 비행 회전 보간 속도. 낮을수록 부드럽지만 선회 반응이 느림. 좌우 떨림 발생 시 줄임 (4~8 권장)")]
    [Range(2f, 15f)] public float rotSmoothSpeed = 6f;

    [Header("봇 한계 (Initialize 시 적용)")]
    [Tooltip("에스코트가 추격 시 낼 수 있는 최대 속도")]
    public float maxSpeedOverride = 200f;
    [Tooltip("선회 속도 (deg/s) — 기본 봇 72°/s를 덮어씀")]
    public float turnRateOverride = 150f;

    [Header("캐터펄트 발진")]
    [Tooltip("발진 시 직진 유지 시간 (최소)")]
    public float launchDuration = 3f;
    [Tooltip("발진 시 직진 목표 속도")]
    public float launchSpeed    = 55f;
    [Tooltip("항모 중심에서 이 수평거리 이상 벗어난 뒤에만 Escorting으로 전환. " +
            "launchDuration이 지나도 항모 위에 있으면 직진 유지 — 측면 비행 리더로 꺾이는 것을 방지. " +
            "폴백: 거리 체크 실패 또는 launchDuration*3 초과 시 강제 전환.")]
    public float carrierClearanceDistance = 180f;

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
    [Tooltip("BeginLanding 호출 시 어프로치 진입 전 자유비행 시간 (플레이어 zone 진입 직후)")]
    public float freeFlightDuration = 4f;
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
    [Tooltip("와이어 체결 순간 부여되는 진입 속도 (m/s) — 실제 접근 속도와 무관하게 급감속 연출에 사용")]
    public float arrestEntrySpeed = 65f;
    [Tooltip("와이어 체결 후 전진 속도 감소율 (m/s²)")]
    public float arrestDecelRate = 80f;
    [Tooltip("체결 후 갑판 Y로 스냅되는 보간 속도 (m/s)")]
    public float arrestYSnapRate = 20f;
    [Tooltip("정지 판정 속도 (이 값 미만이면 Landed 전환)")]
    public float arrestStopSpeed = 0.2f;

    [Header("자연 비행 (마셜·회수 패턴·재합류 공통)")]
    [Tooltip("선회 중 최대 뱅크각 — 선회율에서 협조선회 뱅크를 역산해 이 값으로 제한")]
    public float maxBankDeg      = 65f;
    [Tooltip("마셜·회수 패턴 최대 선회율 (deg/s) — 낮을수록 크게 도는 완만한 선회")]
    public float navTurnRateMax  = 15f;
    [Tooltip("재합류 최대 선회율 (deg/s)")]
    public float rejoinTurnRateMax = 24f;

    [Header("마셜 (조종사 사망 시 항모 상공 대기 선회)")]
    [Tooltip("1번기 대기 고도 (갑판 기준 m) — 기체마다 marshalAltitudeStep 씩 위로 쌓는다")]
    public float marshalBaseAltitude = 400f;
    public float marshalAltitudeStep = 80f;
    [Tooltip("1번기 대기 선회 반경 (m) — 기체마다 marshalRadiusStep 씩 넓힌다")]
    public float marshalRadius       = 1000f;
    public float marshalRadiusStep   = 120f;
    public float marshalSpeed        = 75f;

    [Header("회수 패턴 (Case I — 우현 이니셜 → 좌선회 브레이크 → 다운윈드 → 그루브)")]
    [Tooltip("이니셜 레인: 착함 슬롯 기준 우현 오프셋 (m)")]
    public float patternLaneOffset   = 300f;
    [Tooltip("브레이크 시작 지점: 슬롯 기준 전방 거리 (m)")]
    public float patternBreakAhead   = 600f;
    [Tooltip("브레이크 선회 반경 (m) — 다운윈드 레인 = 레인 오프셋 - 2×반경 (좌현)")]
    public float patternBreakRadius  = 600f;
    [Tooltip("그루브(최종 직선) 길이 (m)")]
    public float patternGrooveLength = 700f;
    [Tooltip("이니셜·브레이크 고도 (갑판 기준 m)")]
    public float patternAltitude     = 300f;
    [Tooltip("다운윈드 끝 고도 (갑판 기준 m)")]
    public float downwindAltitude    = 180f;
    [Tooltip("최종 활공각 (deg)")]
    public float glideSlopeDeg       = 3.5f;
    public float patternSpeed        = 70f;
    public float grooveSpeed         = 52f;
    [Tooltip("앞 기체가 패턴에 진입한 뒤 다음 기체 진입까지 최소 간격 (초)")]
    public float patternSpacing      = 18f;

    [Header("재합류")]
    [Tooltip("편대 슬롯 이 거리 이내 + 기수 정렬 시 편대비행으로 전환")]
    public float rejoinCompleteDistance = 150f;
    [Tooltip("재합류 시 리더보다 최대 얼마나 빨리 따라붙는지 (m/s)")]
    public float rejoinMaxOvertake   = 70f;
    [Tooltip("착함을 마친 뒤 리더가 비행 중이면 재발진까지 대기 (초)")]
    public float relaunchDelay       = 3f;

    public float GetBankOffset(bool isLeft) => isLeft ? bankOffsetDeg : -bankOffsetDeg;
    public float GetSideOffset(bool isLeft) => isLeft ? -landingSideOffset : landingSideOffset;
}
