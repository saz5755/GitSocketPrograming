using UnityEngine;

// 로컬 플레이어 한 틱의 입력과 결과 상태를 저장 (Reconciliation용 히스토리)
public struct TickRecord
{
    public int   tick;
    public float dt;
    public float throttle;         // W=1, S=-1, 없음=0
    public float pitch, yaw, roll; // 해당 틱에 transform.Rotate로 적용된 각도
    public Vector3 velAfter;   // 해당 틱 이후 속도 벡터 (physics Reconciliation용)
    public Vector3 posAfter;
}
