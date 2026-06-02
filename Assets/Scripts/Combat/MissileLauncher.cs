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
        if (GameModeManager.Instance?.IsBoardedInCockpit == true) return;
        if (Input.GetKeyDown(fireKey)) TryFire();

        // 타겟 락온 상태 네트워크 경고 브로드캐스트
        BroadcastLockState();
    }

    void TryFire()
    {
        if (_cooldownTimer > 0f)                                    return;
        if (MissileCount <= 0)                                      return;
        if (_targeting == null || _targeting.LocalPlayer == null)   return;
        if (_targeting.State != TargetingSystem.LockState.Locked)   return;
        if (!_targeting.IsInFireZone)                               return;  // 조준각 초과 시 발사 불가

        _cooldownTimer = cooldown;
        MissileCount--;

        FlightAudioSystem.NotifyMissileFired();
        CreateMissile(_targeting.LocalPlayer.transform, _targeting.Target.transform);
    }

    void CreateMissile(Transform origin, Transform target)
    {
        GameObject prefab = Resources.Load<GameObject>("MissilePrefab");
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, origin.position + origin.forward * 3f + origin.up * -0.4f, origin.rotation);
        }
        else
        {
            go = new GameObject("AIM-120");
            go.transform.position = origin.position + origin.forward * 3f + origin.up * -0.4f;
            go.transform.rotation = origin.rotation;
            go.AddComponent<MissileController>();
        }

        string shooterNick = _targeting.LocalPlayer.nickname;
        string missileId   = System.Guid.NewGuid().ToString("N")[..8];

        var mc = go.GetComponent<MissileController>();
        mc.Initialize(target, origin.GetComponent<PlayerController>()?.CurrentSpeed ?? 30f,
                      MissileGuidanceType.RadarGuided, missileId, shooterNick);

        // 미사일 스폰 네트워크 브로드캐스트
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            var targetPc = target.GetComponent<PlayerController>();
            sc.SendMissileSpawn(missileId, shooterNick,
                targetPc?.nickname ?? "",
                go.transform.position, go.transform.eulerAngles,
                (int)MissileGuidanceType.RadarGuided);
        }
    }

    float _warnTimer;
    void BroadcastLockState()
    {
        if (_targeting == null || _targeting.LocalPlayer == null) return;
        _warnTimer += Time.deltaTime;
        if (_warnTimer < 0.3f) return;
        _warnTimer = 0f;

        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;

        string myNick = _targeting.LocalPlayer.nickname;

        // 타겟팅 상태 경고 (LockState → ThreatLevel 올바른 매핑)
        int lockLevel = _targeting.State switch
        {
            TargetingSystem.LockState.Searching => (int)ThreatWarningSystem.ThreatLevel.Tracked,
            TargetingSystem.LockState.Locked    => (int)ThreatWarningSystem.ThreatLevel.Locked,
            _                                   => (int)ThreatWarningSystem.ThreatLevel.None
        };
        string targetNick = _targeting.Target?.nickname ?? "";
        if (!string.IsNullOrEmpty(targetNick) && lockLevel > 0)
            sc.SendMissileWarn(myNick, targetNick, lockLevel);

        // 발사된 미사일 추적 경고 — ARH 활성 시 MissileActive(5), 중간유도 중 MissileFired(4)
        foreach (var mc in MissileController.All)
        {
            if (mc.ShooterNick != myNick) continue;
            var targetT = mc.Target;
            if (targetT == null) continue;  // 파괴된 오브젝트 안전 처리
            var targetPc = targetT.GetComponent<PlayerController>();
            if (targetPc == null) continue;
            int warnLevel = mc.IsARHActive
                          ? (int)ThreatWarningSystem.ThreatLevel.MissileActive
                          : (int)ThreatWarningSystem.ThreatLevel.MissileFired;
            sc.SendMissileWarn(myNick, targetPc.nickname, warnLevel);
        }
    }
}
