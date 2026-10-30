using System;
using UnityEngine;

/// <summary>
/// 조종 불능 기체(격추·사출 후) — 탄도 낙하 + 무작위 텀블 + 화염/연기 꼬리.
/// 수면이나 갑판·지면에 닿으면 폭발하고 기체를 숨긴 뒤 onImpact 를 호출한다.
/// 로컬 플레이어 잔해는 sendMoves 로 위치를 계속 송신해 다른 클라이언트에서도 떨어지는 모습이 보인다.
/// </summary>
public class WreckController : MonoBehaviour
{
    const float Gravity     = 9.81f;
    const float AirDrag     = 0.12f;   // 수평 속도 감쇠 (1/s)
    const float MaxFallTime = 30f;     // 안전장치 — 무한 낙하 방지
    const float SendInterval = 0.05f;

    Vector3        _vel;
    Vector3        _spin;              // deg/s (로컬 축)
    float          _seaY;
    float          _age;
    float          _sendTimer;
    bool           _sendMoves;
    bool           _impacted;
    Action<Vector3> _onImpact;
    ParticleSystem _smoke, _fire;
    Renderer[]     _renderers;
    readonly RaycastHit[] _hits = new RaycastHit[6];

    public bool Impacted => _impacted;

    /// <summary>aircraft 를 잔해로 전환. 조종/AI 컴포넌트는 호출 측에서 꺼 둔다.</summary>
    public static WreckController Begin(GameObject aircraft, Vector3 velocity, bool onFire,
                                        bool sendMoves, Action<Vector3> onImpact)
    {
        var w = aircraft.GetComponent<WreckController>() ?? aircraft.AddComponent<WreckController>();
        w.enabled    = true;
        w._vel       = velocity;
        w._sendMoves = sendMoves;
        w._onImpact  = onImpact;
        w._impacted  = false;
        w._age       = 0f;
        // 날개 손상 쪽으로 감기는 롤 + 약한 피치·요 — 기체마다 다르게
        w._spin = new Vector3(UnityEngine.Random.Range(-25f, 25f),
                              UnityEngine.Random.Range(-15f, 15f),
                              UnityEngine.Random.Range(90f, 200f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f));
        var ocean = FindAnyObjectByType<OceanSurface>();
        w._seaY = ocean != null ? ocean.seaLevel : 0f;
        w._renderers = aircraft.GetComponentsInChildren<Renderer>();
        w.BuildTrail(onFire);
        return w;
    }

    /// <summary>잔해 상태 해제 — 기체를 다시 보이게 하고 꼬리 연기를 정리 (재출격용).</summary>
    public void Restore()
    {
        SetVisible(true);
        if (_smoke != null) Destroy(_smoke.gameObject);
        if (_fire  != null) Destroy(_fire.gameObject);
        _smoke = _fire = null;
        _impacted = false;
        enabled   = false;
    }

    public void SetVisible(bool visible)
    {
        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in _renderers)
            if (r != null && !(r is ParticleSystemRenderer)) r.enabled = visible;
    }

    /// <summary>원격 기체 뷰처럼 잔해 컴포넌트 없이 가시성만 바꿀 때.</summary>
    public static void SetAircraftVisible(GameObject aircraft, bool visible)
    {
        foreach (var r in aircraft.GetComponentsInChildren<Renderer>())
            if (!(r is ParticleSystemRenderer)) r.enabled = visible;
    }

    /// <summary>즉시 충돌 처리 (수면 충돌·충돌 추락처럼 낙하 없이 끝나는 경우).</summary>
    public void ImpactNow() => Impact(transform.position);

    void Update()
    {
        if (_impacted) return;
        float dt = Time.deltaTime;
        _age += dt;

        Vector3 prev = transform.position;
        _vel.y -= Gravity * dt;
        float drag = Mathf.Exp(-AirDrag * dt);
        _vel.x *= drag; _vel.z *= drag;
        Vector3 next = prev + _vel * dt;

        // 기수는 서서히 속도 방향으로 숙이고, 그 위에 텀블 회전을 얹는다
        if (_vel.sqrMagnitude > 1f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(_vel.normalized, transform.up), 0.6f * dt);
        transform.Rotate(_spin * dt, Space.Self);

        // 이동 구간 사이 지면(갑판 등) 충돌
        Vector3 step = next - prev;
        int n = Physics.RaycastNonAlloc(prev, step.normalized, _hits, step.magnitude + 0.5f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.IsChildOf(transform)) continue;
            Impact(_hits[i].point);
            return;
        }

        transform.position = next;
        if (next.y <= _seaY + 0.5f || _age > MaxFallTime) { Impact(new Vector3(next.x, Mathf.Max(next.y, _seaY), next.z)); return; }

        if (_sendMoves)
        {
            _sendTimer += dt;
            if (_sendTimer >= SendInterval)
            {
                _sendTimer -= SendInterval;
                var sc = NetworkManager.Instance?.socketClient;
                Vector3 e = transform.eulerAngles;
                sc?.SendMove(next.x, next.y, next.z, e.x, e.y, e.z, true, true, sc.GetNextTick());
            }
        }
    }

    void Impact(Vector3 point)
    {
        if (_impacted) return;
        _impacted = true;
        transform.position = point;
        ExplosionEffect.Spawn(point, null, 1.5f);
        FlightAudioSystem.NotifyExplosion(point, false);
        SetVisible(false);
        // 꼬리 연기는 기체에서 떼어 남은 입자가 자연스럽게 사라지게
        if (_fire  != null) { _fire.transform.SetParent(null, true);  _fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);  Destroy(_fire.gameObject, 3f);  _fire = null; }
        if (_smoke != null) { _smoke.transform.SetParent(null, true); _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting); Destroy(_smoke.gameObject, 8f); _smoke = null; }
        _onImpact?.Invoke(point);
    }

    // ── 화염·연기 꼬리 ─────────────────────────────────────────────────────
    // 낙하 속도가 빨라도 끊기지 않게 거리 기준으로 방출 — 하늘에 검은 기둥이 길게 남는다
    void BuildTrail(bool onFire)
    {
        if (_smoke != null) return;

        _smoke = MakeEmitter("WreckSmoke", onFire ? FXLib.SmokeDark : FXLib.Smoke, 700);
        var m = _smoke.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        m.startSpeed    = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        m.startSize     = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.gravityModifier = -0.01f;
        var e = _smoke.emission; e.rateOverTime = 20f; e.rateOverDistance = 0.6f;
        FXLib.ColorOverLife(_smoke, FXLib.Grad(
            new[] { new Color(0.8f, 0.7f, 0.6f), Color.white, new Color(0.85f, 0.85f, 0.85f) }, new[] { 0f, 0.15f, 1f },
            new[] { 0f, 0.9f, 0.45f, 0f }, new[] { 0f, 0.04f, 0.5f, 1f }));
        FXLib.SizeOverLife(_smoke, FXLib.Curve(0f, 0.5f, 0.2f, 1.2f, 1f, 4f));
        _smoke.Play();

        if (!onFire) return;
        _fire = MakeEmitter("WreckFire", FXLib.Glow, 300);
        var fm = _fire.main;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        fm.startSpeed    = new ParticleSystem.MinMaxCurve(1f, 4f);
        fm.startSize     = new ParticleSystem.MinMaxCurve(2f, 4f);
        fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        fm.startColor    = new Color(1f, 0.55f, 0.18f, 1f);
        var fe = _fire.emission; fe.rateOverTime = 60f; fe.rateOverDistance = 0.5f;
        FXLib.ColorOverLife(_fire, FXLib.Grad(
            new[] { Color.white, new Color(1f, 0.45f, 0.1f), new Color(0.5f, 0.08f, 0.02f) }, new[] { 0f, 0.4f, 1f },
            new[] { 1f, 0.7f, 0f }, new[] { 0f, 0.5f, 1f }));
        FXLib.SizeOverLife(_fire, FXLib.Curve(0f, 0.7f, 1f, 1.4f));
        _fire.Play();
    }

    ParticleSystem MakeEmitter(string name, Material mat, int max)
    {
        var ps = FXLib.MakePS(name, transform, mat, max, worldSpace: true, loop: true);
        ps.transform.localPosition = new Vector3(0f, 0.3f, -4f);   // 동체 후방
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.8f;
        return ps;
    }
}
