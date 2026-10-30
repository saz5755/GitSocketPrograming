using UnityEngine;

public struct Snapshot
{
    public Vector3 position;
    public Quaternion rotation;
    public bool isMove;
    public double time;   // NetClock 타임라인 (송신 시각을 로컬로 옮긴 값 — v4.4)
    public byte fx;       // 에어쇼 연출 상태 (스모크 등)
}
