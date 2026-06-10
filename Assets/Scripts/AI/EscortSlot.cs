using UnityEngine;

// 에스코트 편대 슬롯. 하이어라키의 빈 GameObject에 부착해서 슬롯 하나를 정의한다.
// transform.position — 갑판 대기 위치 (이 위치에서 봇이 spawn되고 발진)
// formationOffset — 비행 중 리더(플레이어 항공기) 기준 편대 슬롯 오프셋
//                   X 부호로 좌/우 결정 (음수=Left, 양수=Right) → 뱅크/측면 오프셋 자동 적용
//
// 사용:
//   1. 항모 갑판 위에 빈 GameObject "Slot01" 만들고 EscortSlot 부착
//   2. transform 위치를 갑판 대기 자리로 이동
//   3. formationOffset 설정 (예: (-32, -5, -40) = 리더 좌측 후방)
//   4. AIManager._escortSlotsRoot 에 슬롯들의 부모 GameObject 드래그
//
// V자 4기 예시:
//   Slot01: formationOffset = (-32, -5, -40)   ← 좌측 후방
//   Slot02: formationOffset = ( 32, -5, -40)   ← 우측 후방
//   Slot03: formationOffset = (-60, -8, -80)   ← 좌측 더 뒤
//   Slot04: formationOffset = ( 60, -8, -80)   ← 우측 더 뒤
public class EscortSlot : MonoBehaviour
{
    [Tooltip("비행 중 리더 기준 슬롯 오프셋 (로컬 좌표). X 부호로 좌/우 자동 결정")]
    public Vector3 formationOffset = new Vector3(-32f, -5f, -40f);

    [Tooltip("이 슬롯에서 발진하는 봇 닉네임. 빈 문자열이면 'BOT_E{인덱스}' 자동 생성")]
    public string nickname = "";

    public bool IsLeft => formationOffset.x < 0f;

    // 에디터 가시화 — Scene 뷰에서 슬롯 위치/방향 표시
    void OnDrawGizmos()
    {
        Gizmos.color = IsLeft ? new Color(0.3f, 0.6f, 1f, 0.9f) : new Color(1f, 0.4f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 2f);

        // 편대 오프셋 방향 표시 (리더 forward = transform.forward 기준)
        Vector3 forwardEnd = transform.position + transform.forward * 4f;
        Gizmos.DrawLine(transform.position, forwardEnd);
    }
}
