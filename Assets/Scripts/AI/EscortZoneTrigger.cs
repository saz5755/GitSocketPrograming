using UnityEngine;

// 항공모함 에스코트 존 경계 컴포넌트.
// CarrierController.BuildZones()가 EscortZone 자식 오브젝트에 자동 부착.
// Update()에서 플레이어 위치를 폴링해 진입/이탈 시 EscortAI 콜백을 호출한다.
// 히스테리시스(±50m)로 경계 부근에서 반복 전환(진동)을 방지한다.
[RequireComponent(typeof(SphereCollider))]
public class EscortZoneTrigger : MonoBehaviour
{
    public static EscortZoneTrigger Instance { get; private set; }

    SphereCollider _sphere;
    bool _leaderWasInside;
    bool _prevFlying;

    // 히스테리시스 버퍼: 이 거리만큼의 불감대(dead zone)를 두어 경계 진동 방지
    const float Hysteresis = 50f;

    void Awake()
    {
        Instance = this;
        _sphere  = GetComponent<SphereCollider>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public float Radius => _sphere != null ? _sphere.radius : 800f;

    // 히스테리시스 없는 순수 거리 판정 (외부에서 폴백 용도로 사용)
    public bool IsInsideXZ(Vector3 worldPos)
    {
        if (_sphere == null) return false;
        Vector3 c = transform.position;
        float   r = _sphere.radius;
        float  dx = worldPos.x - c.x;
        float  dz = worldPos.z - c.z;
        return dx * dx + dz * dz <= r * r;
    }

    // 현재 상태에 따라 히스테리시스가 적용된 판정
    // 안에 있을 때는 반경+50m 까지 허용, 밖에 있을 때는 반경-50m 이내여야 진입
    bool IsInsideWithHysteresis(Vector3 worldPos)
    {
        if (_sphere == null) return _leaderWasInside;
        Vector3 c = transform.position;
        float   r = _sphere.radius;
        float  dx = worldPos.x - c.x;
        float  dz = worldPos.z - c.z;
        float distSq = dx * dx + dz * dz;

        float threshold = _leaderWasInside ? (r + Hysteresis) : (r - Hysteresis);
        return distSq <= threshold * threshold;
    }

    void Update()
    {
        var gm = GameModeManager.Instance;
        if (gm == null) return;

        bool flying = gm.IsFlying && gm.LocalAircraft != null;

        // 이륙 첫 프레임: 현재 위치로 상태 동기화만 하고 이벤트 발화 없음
        // (갑판 위 이륙 시 "존 내 진입" 이벤트가 잘못 발화되는 것을 방지)
        if (flying && !_prevFlying)
        {
            _leaderWasInside = IsInsideXZ(gm.LocalAircraft.position);
            _prevFlying = true;
            return;
        }
        _prevFlying = flying;

        if (!flying) return;

        bool inside = IsInsideWithHysteresis(gm.LocalAircraft.position);
        if (inside == _leaderWasInside) return;

        _leaderWasInside = inside;

        foreach (var escort in EscortAI.AllEscorts)
        {
            if (inside) escort.OnLeaderEnteredZone();
            else        escort.OnLeaderExitedZone();
        }

        // 플레이어가 zone 안으로 진입한 순간 — 에스코트 자유비행 → 순차 착함 시퀀스 시작.
        // 플레이어 정지 시점이 아닌 진입 시점에 시작하므로 와이어 정지 동기화 불필요.
        if (inside)
        {
            var carrier = transform.parent != null
                ? transform.parent.GetComponent<CarrierController>()
                : Object.FindObjectOfType<CarrierController>();
            AIManager.Instance?.BeginEscortLanding(carrier != null ? carrier.transform : null);
        }
    }
}
