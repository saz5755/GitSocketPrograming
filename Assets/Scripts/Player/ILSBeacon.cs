using UnityEngine;

/// <summary>
/// 활주로 또는 항모 갑판의 접근 경로 기준 정보를 제공.
/// CarrierController가 갑판 착함 존에 부착하고, FlightHUD가 편차를 읽어 인디케이터를 표시.
/// </summary>
public class ILSBeacon : MonoBehaviour
{
    float     _glideslopeAngle = 3.5f; // 항모 3.5°, 일반 활주로 3°
    Transform _deckTransform;          // forward = 이착륙 방향, right = 우현

    public float GlideslopeAngle => _glideslopeAngle;

    /// <param name="glideslopeAngle">강하각 (도).</param>
    /// <param name="deckTransform">갑판 Transform. forward 방향이 이함 방향.</param>
    public void Configure(float glideslopeAngle, Transform deckTransform)
    {
        _glideslopeAngle = glideslopeAngle;
        _deckTransform   = deckTransform;
    }

    /// <summary>비컨 기준 최대 수신 거리 내에 있는지 확인.</summary>
    public bool IsInRange(Vector3 aircraftPos, float rangeM = 15000f)
        => Vector3.Distance(aircraftPos, transform.position) < rangeM;

    /// <summary>
    /// 수평(로컬라이저) 편차 (m).
    /// +값 = 항공기가 중심선 우측 → 왼쪽으로 수정 필요.
    /// </summary>
    public float LateralDeviation(Vector3 aircraftPos)
    {
        Transform basis = _deckTransform != null ? _deckTransform : transform;
        return Vector3.Dot(aircraftPos - transform.position, basis.right);
    }

    /// <summary>
    /// 수직(글라이드슬로프) 편차 (m).
    /// +값 = 이상 글라이드슬로프 위 → 강하 필요.
    /// </summary>
    public float VerticalDeviation(Vector3 aircraftPos)
    {
        Vector3 flat = new Vector3(aircraftPos.x, transform.position.y, aircraftPos.z);
        float   dist = Vector3.Distance(flat, transform.position);
        float   ideal = transform.position.y
                      + Mathf.Tan(_glideslopeAngle * Mathf.Deg2Rad) * dist;
        return aircraftPos.y - ideal;
    }
}
