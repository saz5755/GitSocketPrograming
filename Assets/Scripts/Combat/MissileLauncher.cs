using UnityEngine;

public class MissileLauncher : MonoBehaviour
{
    [SerializeField] KeyCode fireKey     = KeyCode.Space;
    [SerializeField] float   cooldown    = 1.5f;
    [SerializeField] int     maxMissiles = 4;

    TargetingSystem _targeting;
    float           _cooldownTimer;

    public int MissileCount { get; private set; }

    void Awake()
    {
        _targeting   = GetComponent<TargetingSystem>();
        MissileCount = maxMissiles;
    }

    void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        if (Input.GetKeyDown(fireKey)) TryFire();
    }

    void TryFire()
    {
        if (_cooldownTimer > 0f)                                    return;
        if (MissileCount <= 0)                                      return;
        if (_targeting == null || _targeting.LocalPlayer == null)   return;
        if (_targeting.State != TargetingSystem.LockState.Locked)   return;

        _cooldownTimer = cooldown;
        MissileCount--;

        CreateMissile(_targeting.LocalPlayer.transform, _targeting.Target.transform);
    }

    void CreateMissile(Transform origin, Transform target)
    {
        var go = new GameObject("AIM-120");
        go.transform.position = origin.position
                              + origin.forward * 3f
                              + origin.up      * -0.4f;
        go.transform.rotation = origin.rotation;

        // 동체
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(0.12f, 0.12f, 2.2f);
        Destroy(body.GetComponent<Collider>());

        // 핀 (전방)
        var finH = GameObject.CreatePrimitive(PrimitiveType.Cube);
        finH.name = "FinH";
        finH.transform.SetParent(go.transform, false);
        finH.transform.localPosition = new Vector3(0f, 0f, -0.9f);
        finH.transform.localScale    = new Vector3(0.55f, 0.04f, 0.28f);
        Destroy(finH.GetComponent<Collider>());

        var finV = GameObject.CreatePrimitive(PrimitiveType.Cube);
        finV.name = "FinV";
        finV.transform.SetParent(go.transform, false);
        finV.transform.localPosition = new Vector3(0f, 0f, -0.9f);
        finV.transform.localScale    = new Vector3(0.04f, 0.55f, 0.28f);
        Destroy(finV.GetComponent<Collider>());

        var mc = go.AddComponent<MissileController>();
        mc.Initialize(target, origin.GetComponent<PlayerController>()?.CurrentSpeed ?? 30f);
    }
}
