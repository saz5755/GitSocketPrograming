using UnityEngine;

public enum CountermeasureType { Flare, Chaff }

public class CountermeasureDecoy : MonoBehaviour
{
    public CountermeasureType Type     { get; private set; }
    public Vector3            Position => transform.position;

    float _remaining;

    public void Initialize(CountermeasureType type, float lifetime, Vector3 velocity)
    {
        Type       = type;
        _remaining = lifetime;
        CountermeasureSystem.Register(this);

        // 초기 속도 부여 (항공기 후방으로 드리프트)
        var rb           = gameObject.AddComponent<Rigidbody>();
        rb.useGravity    = false;
        rb.linearDamping          = 1.8f;
        rb.linearVelocity      = velocity;
    }

    void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f) { CountermeasureSystem.Unregister(this); Destroy(gameObject); }
    }

    void OnDestroy() => CountermeasureSystem.Unregister(this);
}
