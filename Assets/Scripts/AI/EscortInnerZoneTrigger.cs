using UnityEngine;

// 에스코트 자유비행 내측 회피 존.
// CarrierController.BuildZones()가 런타임에 자동 부착.
// 이 반경 안쪽에는 자유비행 중 진입하지 않도록 EscortAI가 조향한다.
[RequireComponent(typeof(SphereCollider))]
public class EscortInnerZoneTrigger : MonoBehaviour
{
    public static EscortInnerZoneTrigger Instance { get; private set; }

    SphereCollider _sphere;

    void Awake()
    {
        Instance = this;
        _sphere  = GetComponent<SphereCollider>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public float Radius => _sphere != null ? _sphere.radius : 350f;

    public bool IsInsideXZ(Vector3 worldPos)
    {
        if (_sphere == null) return false;
        Vector3 c  = transform.position;
        float   dx = worldPos.x - c.x;
        float   dz = worldPos.z - c.z;
        return dx * dx + dz * dz <= _sphere.radius * _sphere.radius;
    }
}
