using UnityEngine;

// 항공기 비행 성능 + 네트워크 동기화 튜닝 데이터.
// Project 우클릭 → Create → KF21 → Aircraft → Aircraft Stats 로 에셋 생성 후
// PlayerController._stats 슬롯에 드래그.
//
// 기체별로 다른 SO를 만들어 적용 가능 — KF-21 표준, F-35 둔중형, 훈련용 저속형 등.
[CreateAssetMenu(menuName = "KF21/Aircraft/Aircraft Stats", fileName = "AircraftStats_")]
public class AircraftStatsSO : ScriptableObject
{
    [Header("스로틀")]
    [Tooltip("최대 속도 (m/s). 80 ≈ 288 kph")]
    public float maxSpeed = 80f;
    [Tooltip("가속도 (m/s²). 12 ≈ 0→80 m/s 약 6.7초")]
    public float acceleration = 12f;
    [Tooltip("관성 감속 (m/s²). 입력이 없을 때 자연 감속")]
    public float drag = 6f;

    [Header("마우스 비행 제어")]
    [Tooltip("마우스 Y 픽셀당 피치 각속도 (deg/s)")]
    public float pitchSensitivity = 60f;
    [Tooltip("마우스 X 픽셀당 요 각속도 (deg/s)")]
    public float yawSensitivity = 60f;
    [Tooltip("롤 키 입력 시 각속도 (deg/s)")]
    public float rollSpeed = 85f;

    [Header("네트워크")]
    [Tooltip("UDP 전송 주기 (초). 낮을수록 부드럽지만 대역폭 증가")]
    [Range(0.01f, 0.5f)]
    public float sendInterval = 0.05f;
    [Tooltip("원격 플레이어 보간 지연 (초). 패킷 손실에 대비한 안전 마진")]
    public float interpolationDelay = 0.1f;
    [Tooltip("서버 위치와 클라이언트 예측 위치의 차이 임계값(m). 이상 시 강제 정렬")]
    public float reconcileThreshold = 3f;

    [Header("어레스팅 와이어")]
    [Tooltip("와이어 체결 시 감속 (m/s²). 50 ≈ 240 kph→0 약 1.3초")]
    public float arrestDecel = 50f;

    [Header("카타펄트")]
    [Tooltip("카타펄트 최고속 도달 후 자동 추력 유지 시간 (초)")]
    public float catapultHoldSec = 4f;
    [Tooltip("카타펄트 가속 (m/s²). 180 ≈ 0→72 m/s 약 0.4초")]
    public float catapultAccel = 180f;

    [Header("연료")]
    [Tooltip("최대 연료 (추상 단위). 10000 ≈ 기본 소비율에서 666초 비행")]
    public float maxFuel = 10000f;
    [Tooltip("기본 비행 중 연료 소비율 (/초)")]
    public float fuelConsumption = 15f;
    [Tooltip("애프터버너 사용 중 연료 소비율 (/초)")]
    public float fuelConsumptionAB = 60f;
    [Tooltip("저연료 경고 임계값 비율 (0~1)")]
    [Range(0.05f, 0.5f)] public float lowFuelThreshold = 0.2f;

    [Header("애프터버너")]
    [Tooltip("애프터버너 사용 시 최대속도 배수")]
    [Range(1.1f, 2.5f)] public float afterburnerMultiplier = 1.5f;
    [Tooltip("애프터버너 사용 시 가속 배수")]
    [Range(1f, 3f)]     public float afterburnerAccelMult  = 1.8f;

    [Header("스톨")]
    [Tooltip("이 속도 미만에서 피치 과도 시 스톨 (m/s)")]
    public float stallSpeed = 25f;
    [Tooltip("스톨 판정 피치 각도 (deg). 수평 기준 양수 피치업")]
    public float stallPitchDeg = 25f;
    [Tooltip("스톨 중 자동 노우즈다운 각속도 (deg/s)")]
    public float stallNoseDownRate = 25f;
    [Tooltip("스톨 중 중력 하강 가속 (m/s²)")]
    public float stallFallAccel = 9.8f;

    [Header("데미지")]
    [Tooltip("최대 체력")]
    public float maxHP = 100f;
    [Tooltip("HP 이 비율 이하면 엔진 손상 — 최대속도 저하")]
    [Range(0.1f, 0.6f)] public float engineDamageThreshold = 0.3f;
    [Tooltip("엔진 손상 시 최대속도 배수")]
    [Range(0.3f, 0.9f)] public float engineDamageSpeedMult = 0.7f;
    [Tooltip("HP 이 비율 이하면 중상 — 추가 상태 저하 트리거")]
    [Range(0.05f, 0.3f)] public float criticalDamageThreshold = 0.15f;
}
