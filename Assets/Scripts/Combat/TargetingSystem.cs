using System.Collections.Generic;
using UnityEngine;

public class TargetingSystem : MonoBehaviour
{
    public static readonly List<TargetingSystem> All = new List<TargetingSystem>(8);

    [SerializeField] float   detectRange    = 20000f;
    [SerializeField] float   lockOnDuration = 2.5f;
    [SerializeField] float   fireAngle      = 20f;   // 발사 허용 반각 (degrees)

    public enum LockState { None, Searching, Locked }

    public LockState        State            { get; private set; }
    public PlayerController Target           { get; private set; }
    public PlayerController LocalPlayer      { get; private set; }
    public float            TargetRange      { get; private set; }
    public float            ClosureRate      { get; private set; }  // m/s, positive = closing
    public float            LockProgress     { get; private set; }  // 0..1
    public Vector2          TargetCanvasPos  { get; private set; }  // canvas space (1920x1080, center=0,0)
    public bool             IsTargetOnScreen { get; private set; }
    public float            TargetAspect     { get; private set; }  // degrees
    public bool             IsInFireZone     { get; private set; }  // 기수 방향 fireAngle 이내
    public float            BoresightAngle   { get; private set; }  // 현재 기수~타겟 각도

    readonly List<PlayerController> _enemies = new List<PlayerController>(16);
    float   _scanTimer;
    float   _lockTimer;
    float   _prevRange;
    Camera  _cam;

    void Awake()     => All.Add(this);
    void OnDestroy() => All.Remove(this);

    void Start()
    {
        _cam = Camera.main;
        FindLocal();
    }

    void FindLocal()
    {
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) { LocalPlayer = pc; break; }
    }

    void Update()
    {
        if (LocalPlayer == null || !LocalPlayer.isLocalPlayer) { FindLocal(); if (LocalPlayer == null) return; }
        _cam = Camera.main;

        _scanTimer += Time.deltaTime;
        if (_scanTimer >= 0.4f) { _scanTimer = 0f; RefreshEnemies(); }

        if (SimInput.Pressed(SimAction.LockTarget) && RoomModeSO.WeaponsEnabled) HandleLockInput();

        UpdateLock();
        if (Target != null) UpdateTargetData();
    }

    void RefreshEnemies()
    {
        _enemies.Clear();
        foreach (var pc in PlayerController.All)
            if (IsHostile(pc)) _enemies.Add(pc);
    }

    /// <summary>
    /// 로컬 플레이어 기준 적대 기체 — 비행 중인 적 AI(BOT_N*)와 다른 플레이어(PvP).
    /// 아군 AI(에스코트 BOT_E*, 교대 기체 BOT_SW_* 등)와 격추된 잔해는 제외.
    /// </summary>
    public static bool IsHostile(PlayerController pc)
    {
        if (pc == null || pc.isLocalPlayer || !pc.IsFlying || !pc.gameObject.activeInHierarchy) return false;
        if (string.IsNullOrEmpty(pc.nickname)) return false;
        if (AIManager.IsAIBot(pc.nickname) && !AIManager.IsEnemyBot(pc.nickname)) return false;
        return true;
    }

    /// <summary>
    /// 공중에 떠 있는 기체인가 — 로컬 기체는 PlayerController.IsFlying 이 갱신되지 않으므로 GameModeManager 기준.
    /// </summary>
    public static bool IsAirborne(PlayerController pc)
    {
        if (pc == null || !pc.gameObject.activeInHierarchy) return false;
        if (pc.isLocalPlayer) return GameModeManager.Instance != null && GameModeManager.Instance.IsFlying;
        return pc.IsFlying;
    }

    /// <summary>현재 탐지 중인 적기 목록 (0.4초 간격 갱신)</summary>
    public IReadOnlyList<PlayerController> Enemies => _enemies;

    // FlightHUD에서 비락온 적기 마커 위치 계산에 사용
    public Vector2 WorldToCanvasPos(Vector3 worldPos, out bool onScreen)
    {
        if (_cam == null) { onScreen = false; return Vector2.zero; }
        Vector3 vp = _cam.WorldToViewportPoint(worldPos);
        onScreen = vp.z > 0f && vp.x > 0.01f && vp.x < 0.99f && vp.y > 0.01f && vp.y < 0.99f;
        return new Vector2((vp.x - 0.5f) * 1920f, (vp.y - 0.5f) * 1080f);
    }

    /// <summary>
    /// 화면 밖 표적을 가리킬 화면 가장자리 방향 (canvas 기준, 정규화).
    /// WorldToViewportPoint 는 카메라 뒤쪽 점을 상하좌우 반전시키므로 카메라 로컬 좌표로 계산한다.
    /// </summary>
    public Vector2 EdgeDirection(Vector3 worldPos)
    {
        if (_cam == null) return Vector2.up;
        Vector3 local = _cam.transform.InverseTransformPoint(worldPos);
        var dir = new Vector2(local.x, local.y);
        if (dir.sqrMagnitude < 1e-4f) dir = local.z < 0f ? Vector2.down : Vector2.up;   // 정후방은 아래쪽 화살표
        return dir.normalized;
    }

    void HandleLockInput()
    {
        if (Target == null)
        {
            // 자동 탐지 후 최우선 타겟 락온
            Target     = FindBestTarget();
            _lockTimer = 0f;
            State      = Target != null ? LockState.Searching : LockState.None;
        }
        else
        {
            // 타겟 순환 또는 락온 해제
            int idx = _enemies.IndexOf(Target);
            if (_enemies.Count > 1)
            {
                Target     = _enemies[(idx + 1) % _enemies.Count];
                _lockTimer = 0f;
                State      = LockState.Searching;
            }
            else
            {
                Target     = null;
                State      = LockState.None;
                _lockTimer = 0f;
            }
        }
    }

    void UpdateLock()
    {
        if (Target == null) { State = LockState.None; LockProgress = 0f; return; }

        // 타겟 이탈 또는 범위 초과 시 락온 해제
        if (!_enemies.Contains(Target) ||
            Vector3.Distance(LocalPlayer.transform.position, Target.transform.position) > detectRange * 1.1f)
        {
            Target = null; State = LockState.None; _lockTimer = 0f; LockProgress = 0f;
            return;
        }

        _lockTimer  += Time.deltaTime;
        LockProgress = Mathf.Clamp01(_lockTimer / lockOnDuration);
        State        = _lockTimer >= lockOnDuration ? LockState.Locked : LockState.Searching;
    }

    void UpdateTargetData()
    {
        TargetRange  = Vector3.Distance(LocalPlayer.transform.position, Target.transform.position);
        float dt     = Time.deltaTime;
        ClosureRate  = dt > 0f ? (_prevRange - TargetRange) / dt : 0f;
        _prevRange   = TargetRange;

        // 어스펙트 각: 타겟 기수 vs 우리 방향 (꼬리이면 0°, 정면이면 180°)
        Vector3 targetToUs = LocalPlayer.transform.position - Target.transform.position;
        TargetAspect       = Vector3.Angle(Target.transform.forward, targetToUs);

        // 보어사이트 각: 내 기수 forward vs 타겟 방향 → 발사 가능 여부 판단
        Vector3 toTarget   = (Target.transform.position - LocalPlayer.transform.position).normalized;
        BoresightAngle     = Vector3.Angle(LocalPlayer.transform.forward, toTarget);
        IsInFireZone       = BoresightAngle <= fireAngle;

        if (_cam == null) return;
        Vector3 vp       = _cam.WorldToViewportPoint(Target.transform.position);
        IsTargetOnScreen = vp.z > 0f && vp.x > 0.01f && vp.x < 0.99f && vp.y > 0.01f && vp.y < 0.99f;
        TargetCanvasPos  = new Vector2((vp.x - 0.5f) * 1920f, (vp.y - 0.5f) * 1080f);
    }

    PlayerController FindBestTarget()
    {
        if (LocalPlayer == null) return null;
        PlayerController best  = null;
        float            bestS = float.MaxValue;

        for (int i = 0; i < _enemies.Count; i++)
        {
            float dist = Vector3.Distance(LocalPlayer.transform.position, _enemies[i].transform.position);
            if (dist > detectRange) continue;

            // 화면 중앙 근접도 우선, 거리 보조
            float score = dist;
            if (_cam != null)
            {
                Vector3 vp = _cam.WorldToViewportPoint(_enemies[i].transform.position);
                score = vp.z > 0f
                    ? Vector2.Distance(new Vector2(vp.x, vp.y), new Vector2(0.5f, 0.5f)) * 8000f + dist
                    : float.MaxValue;
            }

            if (score < bestS) { bestS = score; best = _enemies[i]; }
        }
        return best;
    }
}
