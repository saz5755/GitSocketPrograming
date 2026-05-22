using UnityEngine;

public class MissileController : MonoBehaviour
{
    [SerializeField] float topSpeed      = 600f;
    [SerializeField] float acceleration  = 150f;
    [SerializeField] float navConst      = 4f;    // 비례항법 상수 N
    [SerializeField] float maxTurnRate   = 30f;   // deg/s
    [SerializeField] float proximityFuse = 15f;   // m
    [SerializeField] float lifetime      = 30f;   // s
    [SerializeField] float armDelay      = 0.5f;  // s

    Transform _target;
    float     _speed;
    float     _age;
    Vector3   _prevLOS;
    bool      _armed;

    public void Initialize(Transform target, float launchSpeed)
    {
        _target = target;
        _speed  = Mathf.Max(launchSpeed, 40f);
    }

    void Update()
    {
        _age += Time.deltaTime;
        if (_age > lifetime) { Destroy(gameObject); return; }
        if (_age > armDelay) _armed = true;

        _speed = Mathf.MoveTowards(_speed, topSpeed, acceleration * Time.deltaTime);

        if (_target != null)
            Navigate();
        else
            transform.position += transform.forward * _speed * Time.deltaTime;

        // 근접 신관
        if (_armed && _target != null &&
            Vector3.Distance(transform.position, _target.position) <= proximityFuse)
            Detonate();
    }

    void Navigate()
    {
        Vector3 LOS = (_target.position - transform.position).normalized;
        float   dt  = Time.deltaTime;

        if (dt > 0f && _prevLOS.sqrMagnitude > 0.001f)
        {
            // 비례항법: 지령가속도 = N * 폐쇄속도 * LOS변화율
            Vector3 LOSRate    = (LOS - _prevLOS) / dt;
            float   closingSpd = Vector3.Dot(transform.forward * _speed, LOS);
            Vector3 desired    = (transform.forward + navConst * closingSpd * LOSRate * dt).normalized;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(desired),
                maxTurnRate * dt);
        }

        _prevLOS           = LOS;
        transform.position += transform.forward * _speed * dt;
    }

    void Detonate()
    {
        Debug.Log($"[Missile] Detonated near {_target?.name}");
        // TODO: 폭발 이펙트, 데미지 적용
        Destroy(gameObject);
    }
}
