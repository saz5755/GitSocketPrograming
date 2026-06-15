using UnityEngine;

// 어레스팅 와이어 시스템 튜닝 데이터.
// Project 우클릭 → Create → KF21 → Carrier → Wire System 으로 에셋 생성 후
// ArrestingWireSystem._config 슬롯에 드래그.
[CreateAssetMenu(menuName = "KF21/Carrier/Wire System", fileName = "WireSystem_")]
public class WireSystemSO : ScriptableObject
{
    [Header("와이어 위치 (모함 로컬 좌표)")]
    [Tooltip("Wire 1 — 가장 뒤쪽 (Bow 방향 기준)")]
    public Vector3 wire1LocalPos = new Vector3(0, -2f, 50f);
    [Tooltip("Wire 2 — 중간")]
    public Vector3 wire2LocalPos = new Vector3(0, -2f, 45f);
    [Tooltip("Wire 3 — 가장 앞쪽 (Bow 방향 기준)")]
    public Vector3 wire3LocalPos = new Vector3(0, -2f, 40f);
    [Tooltip("갑판 사선 각도 (실제 항모 갑판은 함체 중심선에서 약 7~9° 기울어짐)")]
    public float deckAngleDeg = -7f;

    [Header("트리거 볼륨")]
    [Tooltip("와이어 가로 폭 (m)")]
    public float wireWidth = 22f;
    [Tooltip("와이어 두께 — Z방향 깊이 (m)")]
    public float triggerDepth = 3f;
    [Tooltip("와이어 위 트리거 높이 (m)")]
    public float triggerHeight = 5f;

    [Header("착함 높이 여유")]
    [Tooltip("갑판 면에서 항공기 피벗까지의 오프셋 (m). 음수 = 피벗이 갑판 아래 — KF21 모델 바닥이 피벗보다 위에 있을 때 필요")]
    public float landingClearance = -0.097f;

    [Header("체결 동작")]
    [Tooltip("이 속도 미만이면 와이어가 체결되지 않음 (너무 느리면 와이어 통과)")]
    public float minCatchSpeed = 15f;
    [Tooltip("체결 후 이 속도 미만으로 떨어지면 정지 처리")]
    public float stopThreshold = 0.5f;
    [Tooltip("정지 후 와이어가 다시 활성화될 때까지 대기 시간 (초)")]
    public float resetDelay = 4f;

    [Header("머티리얼 (미할당 시 런타임 합성 폴백)")]
    [Tooltip("케이블 본체/볼라드 강철 머티리얼. 비워두면 갑판 머티리얼을 복사해 강철 조정")]
    public Material wireSteelMaterial;
    [Tooltip("V자 LineRenderer 머티리얼. 비워두면 Unlit 셰이더로 합성")]
    public Material wireRopeMaterial;
    [Tooltip("존 디스크 투명 머티리얼. 비워두면 Transparent 셰이더로 합성")]
    public Material zoneDiscMaterial;

    [Header("비주얼 표시 토글 (게임 외관 정리용)")]
    [Tooltip("V자 LineRenderer를 표시할지. 와이어 케이블만 보이게 하려면 끔")]
    public bool showLineRenderer = true;
    [Tooltip("보라색 존 디스크를 표시할지. 게임 빌드에서는 보통 비활성")]
    public bool showZoneDisc = false;
    [Tooltip("와이어 위 텍스트 라벨을 표시할지. 디버깅용")]
    public bool showZoneLabel = false;

    [Header("케이블 Emission 색 (HDR)")]
    [ColorUsage(true, true)] public Color emissionAvailable = new Color(0.12f, 0.09f, 0.00f);
    [ColorUsage(true, true)] public Color emissionCaught    = new Color(2.50f, 0.50f, 0.00f);
    [ColorUsage(true, true)] public Color emissionStopped   = Color.black;

    [Header("LineRenderer 색")]
    public Color lineColorAvailable = new Color(0.90f, 0.80f, 0.55f);
    public Color lineColorCaught    = new Color(1.00f, 0.35f, 0.00f);
    public Color lineColorStopped   = new Color(0.45f, 0.45f, 0.45f);

    [Header("Zone Disc 색")]
    public Color zoneColorAvailable = new Color(0.10f, 0.10f, 0.10f, 0.10f);
    public Color zoneColorCaught    = new Color(1.00f, 0.40f, 1.00f, 1.00f);
    public Color zoneColorStopped   = new Color(0.50f, 0.50f, 0.50f, 0.35f);
}
