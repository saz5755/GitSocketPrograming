using UnityEngine;

public enum CountermeasureType { Flare, Chaff }

public class CountermeasureDecoy : MonoBehaviour
{
    public CountermeasureType Type     { get; private set; }
    public Vector3            Position => transform.position;
    // 채프 도플러 계산용 — 투하 시 항공기 속도를 기준으로 하며,
    // 이후 공기 저항으로 감속되는 실시간 속도를 추적
    public Vector3            Velocity { get; private set; }

    float     _remaining;
    Rigidbody _rb;

    public void Initialize(CountermeasureType type, float lifetime, Vector3 velocity)
    {
        Type       = type;
        _remaining = lifetime;
        Velocity   = velocity;
        CountermeasureSystem.Register(this);

        _rb              = gameObject.AddComponent<Rigidbody>();
        _rb.useGravity   = false;
        _rb.linearDamping = 1.8f;
        _rb.linearVelocity = velocity;
    }

    void Update()
    {
        // 실시간 속도 추적 (공기 저항으로 감속 반영)
        if (_rb != null) Velocity = _rb.linearVelocity;

        _remaining -= Time.deltaTime;
        if (_remaining <= 0f) { CountermeasureSystem.Unregister(this); Destroy(gameObject); }
    }

    void OnDestroy() => CountermeasureSystem.Unregister(this);
}
