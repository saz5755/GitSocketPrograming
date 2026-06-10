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
}
