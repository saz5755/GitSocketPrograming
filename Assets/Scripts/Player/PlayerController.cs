using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public static readonly List<PlayerController> All = new List<PlayerController>(16);

    public string nickname;
    public bool   isLocalPlayer;
    public bool   IsFlying { get; set; }
    public int    lastReceivedTick;

    List<Snapshot> snapshots = new();

    static readonly int s_hashMove = Animator.StringToHash("Move");

    Animator anim;
    bool     _hasMoveParam;
    float    moveSendTimer;
    int      localTick;

    [Header("동작 튜닝 데이터")]
    [Tooltip("AircraftStatsSO 에셋. Project 우클릭 → Create → KF21 → Aircraft → Aircraft Stats")]
    [SerializeField] AircraftStatsSO _stats;

    // SO 미할당 시 폴백 인스턴스 — 한 번만 생성
    static AircraftStatsSO s_fallbackStats;
    AircraftStatsSO Cfg
    {
        get
        {
            if (_stats != null) return _stats;
            if (s_fallbackStats == null)
            {
                s_fallbackStats = ScriptableObject.CreateInstance<AircraftStatsSO>();
                if (Application.isPlaying)
                    Debug.LogWarning(
                        $"[PlayerController] '{name}' has no AircraftStatsSO assigned — using built-in defaults. " +
                        "Create one via Project window → Right Click → Create → KF21 → Aircraft → Aircraft Stats."
                    );
            }
            return s_fallbackStats;
        }
    }

    float currentSpeed;
    public float CurrentSpeed => currentSpeed;

    // ── 어레스팅 와이어 감속 ─────────────────────────────────────────────────
    // 실제 MK-7 계열 어레스팅 엔진처럼 진입 속도와 무관하게 일정 런아웃 거리 안에서 세운다:
    //   ① 케이블이 늘어나며 감속이 0.2초에 걸쳐 차오름 → ② 거의 일정한 감속(정속 런아웃)
    //   ③ 저속에서 부드럽게 풀림 → ④ 정지 후 케이블 장력에 1m 남짓 뒤로 끌려가는 롤백
    const float ArrestOnsetTime   = 0.2f;
    const float ArrestTailSpeed   = 6f;
    const float RollbackDuration  = 1.1f;
    const float RollbackPeakSpeed = 1.5f;

    bool  _arrested;
    float _arrestT, _arrestV0, _arrestDecelNow;
    float _rollbackT = -1f;      // < 0 이면 롤백 전
    bool  _arrestSettled;

    public bool  IsArrested      => _arrested;
    /// <summary>롤백까지 끝나 기체가 완전히 멈춘 상태</summary>
    public bool  IsArrestSettled => _arrested && _arrestSettled;
    /// <summary>현재 어레스팅 감속도 (m/s²) — 기수 숙임 등 연출용</summary>
    public float ArrestDecel     => _arrested ? _arrestDecelNow : 0f;
    /// <summary>롤백 중 후진 속도 (m/s, 양수) — 연출용</summary>
    public float ArrestRollbackSpeed { get; private set; }

    public void BeginArrest()
    {
        if (_arrested) return;   // 와이어/트랩 경로 중복 호출 시 프로파일 리셋 방지
        _arrested       = true;
        _arrestT        = 0f;
        _arrestV0       = Mathf.Max(currentSpeed, 1f);
        _arrestDecelNow = 0f;
        _rollbackT      = -1f;
        _arrestSettled  = false;
        ArrestRollbackSpeed = 0f;
    }
    public void EndArrest()
    {
        _arrested = false;
        _arrestDecelNow = 0f;
        ArrestRollbackSpeed = 0f;
    }

    // 어레스팅 감속 1스텝 — 이동량(전진 +, 롤백 -)을 반환
    float StepArrest(float dt)
    {
        _arrestT += dt;

        if (_rollbackT < 0f)
        {
            float a = Mathf.Min(_arrestV0 * _arrestV0 / (2f * Cfg.arrestRunout), Cfg.arrestDecel);
            a *= Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, _arrestT / ArrestOnsetTime));
            a *= Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(currentSpeed / ArrestTailSpeed));
            _arrestDecelNow = a;

            currentSpeed = Mathf.Max(0f, currentSpeed - a * dt);
            if (currentSpeed <= 0.05f) { currentSpeed = 0f; _rollbackT = 0f; }
            return currentSpeed * dt;
        }

        // 롤백: 늘어난 케이블이 기체를 뒤로 당겼다가 놓음 (사인 반주기 속도 프로파일)
        _arrestDecelNow = 0f;
        if (_rollbackT >= RollbackDuration) { ArrestRollbackSpeed = 0f; _arrestSettled = true; return 0f; }
        _rollbackT += dt;
        ArrestRollbackSpeed = RollbackPeakSpeed * Mathf.Sin(Mathf.Clamp01(_rollbackT / RollbackDuration) * Mathf.PI);
        return -ArrestRollbackSpeed * dt;
    }

    // ── 카타펄트 발진 ─────────────────────────────────────────────────────────
    bool  _catapulting;
    float _catapultTarget;
    float _catapultAccel;
    float _catapultHoldTimer;

    public void StartCatapult(float targetSpeed)
    {
        currentSpeed       = 0f;
        _catapultTarget    = targetSpeed;
        _catapultAccel     = Cfg.catapultAccel;
        _catapulting       = true;
        _catapultHoldTimer = 0f;
    }

    // ── 연료 ───────────────────────────────────────────────────────────────────
    float _currentFuel = -1f; // -1: 미초기화 (첫 Update에서 maxFuel로 초기화)
    public float CurrentFuel => _currentFuel < 0f ? Cfg.maxFuel : _currentFuel;
    public float MaxFuel     => Cfg.maxFuel;
    public bool  IsFuelLow   => CurrentFuel <= Cfg.maxFuel * Cfg.lowFuelThreshold;
    public bool  IsFuelEmpty => CurrentFuel <= 0.01f;
    public void  Refuel()    { _currentFuel = Cfg.maxFuel; }

    // ── 애프터버너 ──────────────────────────────────────────────────────────────
    bool _afterburner;
    public bool AfterburnerActive => _afterburner && !IsFuelEmpty;

    // ── 스톨 ─────────────────────────────────────────────────────────────────
    bool _stallWarning;
    bool _isStalling;
    public bool StallWarning => _stallWarning;
    public bool IsStalling   => _isStalling;

    // ── 데미지 ─────────────────────────────────────────────────────────────────
    float _currentHP = -1f;
    public float CurrentHP => _currentHP < 0f ? Cfg.maxHP : _currentHP;
    public float MaxHP     => Cfg.maxHP;
    public bool  IsEngineDamaged  => CurrentHP <= Cfg.maxHP * Cfg.engineDamageThreshold;
    public bool  IsCriticalDamage => CurrentHP <= Cfg.maxHP * Cfg.criticalDamageThreshold;
    public bool  IsDestroyed      => CurrentHP <= 0.01f;

    /// <summary>로컬 플레이어 피격 시 호출. 네트워크는 피격자 측에서 로컬 계산.</summary>
    public void TakeDamage(float amount)
    {
        if (_currentHP < 0f) _currentHP = Cfg.maxHP;
        _currentHP = Mathf.Max(0f, _currentHP - amount);
    }

    public void Repair() { _currentHP = Cfg.maxHP; }

    /// <summary>새 출격 — 기체를 정비 완료 상태(체력·연료 만탄, 정지, 기동/제동 상태 해제)로 되돌린다.</summary>
    public void ResetForNewSortie()
    {
        Repair();
        Refuel();
        currentSpeed   = 0f;
        _catapulting   = false;
        _afterburner   = false;
        _stallWarning  = false;
        _isStalling    = false;
        _barrelRollDir = 0;
        _cobraPhase    = CobraInactive;
        EndArrest();
        ClearSnapshots();
    }

    // ── 자동 기동: 배럴롤 + Immelmann (GameplayConfigSO 중앙 관리) ───────────
    // 모든 수치는 Assets/Resources/GameplayConfig.asset에서 조정.
    // Config 미할당 시 하드코딩 기본값 사용 (SO.Instance가 자동 폴백).
    GameplayConfigSO GCfg => GameplayConfigSO.Instance;

    const int CobraInactive = -1;
    // 배럴롤 방향: 0=정지, +1=좌(Q 홀드), -1=우(E 홀드)
    int   _barrelRollDir;
    float _barrelFlareTimer;
    int   _cobraPhase = CobraInactive;
    float _cobraPhaseTimer;

    public bool IsBarrelRolling => _barrelRollDir != 0;

    public bool IsCobraActive       => _cobraPhase != CobraInactive;
    public bool IsCobraRising       => _cobraPhase == 0 || _cobraPhase == 1;
    public bool IsImmelmannRollover => _cobraPhase == 2;

    // 자동 기동용 CountermeasureSystem 참조.
    // CMS는 FlightHUD의 GameObject에 AddComponent되므로 PlayerController와 다른 오브젝트.
    // → 씬에서 FindAnyObjectByType으로 획득 (로컬 플레이어 전용이라 한 개만 존재).
    CountermeasureSystem _cms;
    CountermeasureSystem GetCMS()
    {
        if (_cms == null) _cms = FindAnyObjectByType<CountermeasureSystem>();
        return _cms;
    }
    void TryAutoFlare() => GetCMS()?.TryDeployAutoBurst();

    const int MaxHistory = 64;
    readonly Queue<TickRecord>  _inputHistory = new();
    readonly Queue<TickRecord>  _reconScratch = new();   // HandleMoveAck 재사용 — 매 ACK마다 new 할당 방지
    readonly RaycastHit[]       _groundHits   = new RaycastHit[8];

    void Awake()
    {
        VaporTrailRig.Ensure(transform);   // 고속 유지 시 날개 끝 증기 꼬리 (KF21 기체만)
        anim = GetComponent<Animator>();
        if (anim != null)
        {
            foreach (var p in anim.parameters)
                if (p.name == "Move") { _hasMoveParam = true; break; }
        }
        All.Add(this);
    }

    void OnEnable()
    {
        if (!isLocalPlayer) return;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnMoveAck += HandleMoveAck;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void OnDisable()
    {
        if (!isLocalPlayer) return;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnMoveAck -= HandleMoveAck;
    }

    void OnDestroy()
    {
        All.Remove(this);
        if (!isLocalPlayer) return;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnMoveAck -= HandleMoveAck;
    }

    void Update()
    {
        if (isLocalPlayer) LocalUpdate();
        else               RemoteUpdate();
    }

    // ── 로컬 플레이어 ──────────────────────────────────────────────────────────
    void LocalUpdate()
    {
        float dt = Time.deltaTime;

        // 어레스팅 와이어 감속 중 — 입력 차단, 강제 감속
        if (_arrested)
        {
            float step = StepArrest(dt);
            // 롤백은 지면 방향(수평)으로만 — 수평 복귀 중인 피치가 끼어들지 않게
            Vector3 fwd = transform.forward;
            if (step < 0f) { fwd.y = 0f; fwd.Normalize(); }
            transform.position += fwd * step;
            ConstrainToSurface();

            moveSendTimer += dt;
            if (moveSendTimer >= Cfg.sendInterval)
            {
                moveSendTimer -= Cfg.sendInterval;
                localTick = NetworkManager.Instance.socketClient.GetNextTick();
                Vector3 arrestEuler = transform.eulerAngles;
                NetworkManager.Instance.socketClient.SendMove(
                    transform.position.x, transform.position.y, transform.position.z,
                    arrestEuler.x, arrestEuler.y, arrestEuler.z,
                    currentSpeed > 0.5f,
                    GameModeManager.Instance != null && GameModeManager.Instance.IsFlying,
                    localTick);
            }
            return;
        }

        // 카타펄트 발진 — 속도·위치는 자동, 회전(pitch/yaw/roll)은 즉시 허용
        if (_catapulting)
        {
            if (currentSpeed < _catapultTarget)
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, _catapultTarget, _catapultAccel * dt);
            }
            else
            {
                currentSpeed = _catapultTarget;
                _catapultHoldTimer += dt;
                if (_catapultHoldTimer >= Cfg.catapultHoldSec)
                    _catapulting = false;
            }

            bool freeLookC = Input.GetMouseButton(1);
            float pitchC   = freeLookC ? 0f : -Input.GetAxis("Mouse Y") * Cfg.pitchSensitivity * dt;
            float yawC     = freeLookC ? 0f :  Input.GetAxis("Mouse X") * Cfg.yawSensitivity   * dt;
            float rollC    = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q)) rollC =  Cfg.rollSpeed * dt;
            if (Input.GetKey(KeyCode.D) || (Input.GetKey(KeyCode.E) && !SimInput.Modifier)) rollC = -Cfg.rollSpeed * dt;
            transform.Rotate(pitchC, yawC, rollC, Space.Self);

            float yBeforeC = transform.position.y;
            transform.position += transform.forward * currentSpeed * dt;
            // 사출 중 기수를 내려도 갑판을 뚫지 않도록 (기존: 이 분기만 갑판 검사가 없어 그대로 관통)
            ConstrainToSurface(yBeforeC);
            if (anim != null) anim.SetBool(s_hashMove, true);

            moveSendTimer += dt;
            if (moveSendTimer >= Cfg.sendInterval)
            {
                moveSendTimer -= Cfg.sendInterval;
                localTick = NetworkManager.Instance.socketClient.GetNextTick();
                Vector3 catEuler = transform.eulerAngles;
                NetworkManager.Instance.socketClient.SendMove(
                    transform.position.x, transform.position.y, transform.position.z,
                    catEuler.x, catEuler.y, catEuler.z,
                    true,
                    GameModeManager.Instance != null && GameModeManager.Instance.IsFlying,
                    localTick);
            }
            return;
        }

        // ── 자동 기동 입력 처리 ────────────────────────────────────────────
        // 다른 자동/강제 상태 중이 아니고 비행 중일 때만 자동 기동 시작 가능.
        bool canStartManeuver = GameModeManager.Instance != null && GameModeManager.Instance.IsFlying;

        // Q/E 홀드: Q=왼쪽 배럴롤, E=오른쪽 배럴롤 + 플레어 자동 발사
        // 코브라 중이 아닐 때만 활성 (상호 배타). 두 키 동시 홀드 시 Q 우선.
        int newBarrelDir = 0;
        if (canStartManeuver && _cobraPhase == CobraInactive)
        {
            if      (Input.GetKey(KeyCode.Q)) newBarrelDir = +1;  // 좌
            else if (Input.GetKey(KeyCode.E) && !SimInput.Modifier) newBarrelDir = -1;  // 우 (Ctrl+E 는 사출 핸들)
        }
        if (newBarrelDir != 0 && _barrelRollDir == 0) _barrelFlareTimer = 0f; // 홀드 시작 시 즉시 첫 플레어
        _barrelRollDir = newBarrelDir;

        // B 입력: 홀드 중 재입력이면 Phase 2로, 비활성이면 Phase 0 시작
        if (Input.GetKeyDown(KeyCode.B) && canStartManeuver && _barrelRollDir == 0)
        {
            if (_cobraPhase == 1)           // 수직 유지 중 재입력 → Immelmann flip
            {
                _cobraPhase      = 2;
                _cobraPhaseTimer = 0f;
            }
            else if (_cobraPhase == CobraInactive)
            {
                _cobraPhase      = 0;       // 급 피치업 시작
                _cobraPhaseTimer = 0f;
            }
        }

        // ── 2단계 Immelmann: Phase 0 피치업 → Phase 1 수직유지(대기) → Phase 2 flip ──
        if (_cobraPhase != CobraInactive)
        {
            _cobraPhaseTimer += dt;
            switch (_cobraPhase)
            {
                case 0: // 급 피치업
                    transform.Rotate(-GCfg.cobraPitchRate * dt, 0f, 0f, Space.Self);
                    currentSpeed = Mathf.MoveTowards(currentSpeed, 25f, 120f * dt);
                    if (_cobraPhaseTimer >= GCfg.cobraPhase0Duration)
                        { _cobraPhase = 1; _cobraPhaseTimer = 0f; }
                    break;
                case 1: // 수직 자세 유지 + B 재입력 대기 (타임아웃)
                    currentSpeed = Mathf.MoveTowards(currentSpeed, 15f, 60f * dt);
                    if (_cobraPhaseTimer >= GCfg.cobraHoldTimeout)
                        { _cobraPhase = CobraInactive; _cobraPhaseTimer = 0f; }
                    break;
                case 2: // Immelmann flip: half-loop + half-roll 동시 → 반대 방향 수평
                    transform.Rotate(-GCfg.immelmannLoopRate * dt, 0f, -GCfg.immelmannRollRate * dt, Space.Self);
                    currentSpeed = Mathf.MoveTowards(currentSpeed, 60f, 80f * dt);
                    if (_cobraPhaseTimer >= GCfg.immelmannPhase2Duration)
                        { _cobraPhase = CobraInactive; _cobraPhaseTimer = 0f; }
                    break;
            }
            transform.position += transform.forward * currentSpeed * dt;

            SendMoveTick(dt, true);
            return;
        }

        // ── 연료 초기화 (lazy) ────────────────────────────────────────────────
        if (_currentFuel < 0f) _currentFuel = Cfg.maxFuel;
        if (_currentHP   < 0f) _currentHP   = Cfg.maxHP;

        // ── 애프터버너 입력 (Shift 홀드), 연료 있어야 작동 ───────────────────
        bool abRequested = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        _afterburner = abRequested && !IsFuelEmpty && GameModeManager.Instance != null && GameModeManager.Instance.IsFlying;

        float throttle = 0f;
        if (Input.GetKey(KeyCode.W))      throttle =  1f;
        else if (Input.GetKey(KeyCode.S)) throttle = -1f;

        // ── 최대속도·가속: 엔진 손상·AB 반영 ────────────────────────────────
        float maxSpeedEff = Cfg.maxSpeed;
        if (IsEngineDamaged) maxSpeedEff *= Cfg.engineDamageSpeedMult;
        float accelEff = Cfg.acceleration;
        if (AfterburnerActive)
        {
            maxSpeedEff *= Cfg.afterburnerMultiplier;
            accelEff    *= Cfg.afterburnerAccelMult;
        }

        // 연료 고갈: 추력 없음, drag만 작용
        bool noThrust = IsFuelEmpty || IsDestroyed;

        if (noThrust)
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Cfg.drag * dt);
        else if (throttle > 0f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeedEff, accelEff * dt);
        else if (throttle < 0f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Cfg.acceleration * dt);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Cfg.drag * dt);

        // ── 연료 소비 (비행 중에만) ──────────────────────────────────────────
        bool flying = GameModeManager.Instance != null && GameModeManager.Instance.IsFlying;
        if (flying && !IsFuelEmpty)
        {
            float consume = AfterburnerActive ? Cfg.fuelConsumptionAB : Cfg.fuelConsumption;
            _currentFuel = Mathf.Max(0f, _currentFuel - consume * dt);
        }

        bool freeLook = Input.GetMouseButton(1);
        float pitch = freeLook ? 0f : -Input.GetAxis("Mouse Y") * Cfg.pitchSensitivity * dt;
        float yaw   = freeLook ? 0f :  Input.GetAxis("Mouse X") * Cfg.yawSensitivity   * dt;

        // 수동 롤: A=좌, D=우 (Q/E는 자동 배럴롤 전용으로 분리)
        float roll = 0f;
        if (Input.GetKey(KeyCode.A)) roll =  Cfg.rollSpeed * dt;
        if (Input.GetKey(KeyCode.D)) roll = -Cfg.rollSpeed * dt;

        // 자동 배럴롤: Q(+1)=좌, E(-1)=우. 사용자 A/D와 덧붙음 (반대 입력으로 자전 느려짐)
        // 플레어는 아래 Rotate 이후 Interval 간격으로 자동 발사
        if (_barrelRollDir != 0) roll += _barrelRollDir * GCfg.barrelRollRate * dt;

        // ── 스톨 체크: 저속 + 과도 피치업 → 자동 노우즈다운 + 중력 하강 ──────
        // 피치각 (수평 기준 노우즈 각도, +값=상승)
        float pitchAngle = -NormalizeAngle(transform.eulerAngles.x);
        _stallWarning = flying && currentSpeed < Cfg.stallSpeed * 1.5f && pitchAngle > Cfg.stallPitchDeg * 0.6f;
        _isStalling   = flying && currentSpeed < Cfg.stallSpeed       && pitchAngle > Cfg.stallPitchDeg;
        if (_isStalling)
        {
            // 자동 노우즈다운 (양력 손실) — 플레이어 입력 pitch는 유지하되 추가로 아래로 꺾음
            pitch -= Cfg.stallNoseDownRate * dt;
        }

        transform.Rotate(pitch, yaw, roll, Space.Self);
        float yBefore = transform.position.y;
        transform.position += transform.forward * currentSpeed * dt;

        // 스톨 중: 중력 하강 추가 적용
        if (_isStalling)
        {
            transform.position += Vector3.down * Cfg.stallFallAccel * dt;
        }
        // 실속 속도 미만이면 피치와 무관하게 양력 부족으로 침하 (기존: 수평 자세로 감속하면 속도 0 으로 공중 정지).
        // 갑판 위 저속 주행은 제외 — 바로 아래에 지면이 있으면 침하하지 않는다.
        else if (flying && currentSpeed < Cfg.stallSpeed && !HasSurfaceBelow(4f))
        {
            float deficit = 1f - currentSpeed / Cfg.stallSpeed;
            transform.position += Vector3.down * Cfg.stallFallAccel * deficit * dt;
            _stallWarning = true;
        }
        if (flying) ClampAboveSea();

        if (currentSpeed > 0.1f) ConstrainToSurface(yBefore);

        if (anim != null) anim.SetBool(s_hashMove, currentSpeed > 0.5f);

        // Q/E 홀드 중 플레어 자동 발사: GameplayConfig 간격/burst 사용
        if (_barrelRollDir != 0)
        {
            _barrelFlareTimer -= dt;
            if (_barrelFlareTimer <= 0f)
            {
                TryAutoFlare();
                _barrelFlareTimer = GCfg.autoFlareInterval;
            }
        }

        moveSendTimer += dt;
        if (moveSendTimer >= Cfg.sendInterval)
        {
            moveSendTimer -= Cfg.sendInterval;
            localTick = NetworkManager.Instance.socketClient.GetNextTick();

            if (_inputHistory.Count >= MaxHistory) _inputHistory.Dequeue();
            _inputHistory.Enqueue(new TickRecord
            {
                tick       = localTick,
                dt         = Cfg.sendInterval,
                throttle   = throttle,
                pitch      = pitch, yaw = yaw, roll = roll,
                velAfter   = transform.forward * currentSpeed,
                posAfter   = transform.position
            });

            Vector3 euler = transform.eulerAngles;
            NetworkManager.Instance.socketClient.SendMove(
                transform.position.x, transform.position.y, transform.position.z,
                euler.x, euler.y, euler.z,
                currentSpeed > 0.5f,
                GameModeManager.Instance != null && GameModeManager.Instance.IsFlying,
                localTick);
        }
    }

    // ── 지면/갑판 관통 방지 ────────────────────────────────────────────────────────
    bool HasSurfaceBelow(float range)
    {
        int count = Physics.RaycastNonAlloc(
            transform.position + Vector3.up * 1f, Vector3.down,
            _groundHits, range + 1f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!_groundHits[i].transform.IsChildOf(transform)) return true;
        return false;
    }

    // 바다에는 콜라이더가 없어 기체가 해수면 아래로 그대로 통과한다 — 수면에서 멈춘다 (추락 처리는 별도 과제)
    OceanSurface _ocean;
    bool _oceanSearched;
    void ClampAboveSea()
    {
        if (!_oceanSearched) { _ocean = FindAnyObjectByType<OceanSurface>(); _oceanSearched = true; }
        if (_ocean == null) return;
        float minY = _ocean.seaLevel + 1.5f;
        if (transform.position.y < minY)
        {
            var p = transform.position; p.y = minY; transform.position = p;
        }
    }

    // yBefore: 이번 프레임 이동 전 높이. 이동 전~후 구간을 훑어 한 프레임에 크게 내려가도 갑판을 놓치지 않는다
    // (기존: 현재 위치 위 3m 에서만 쏴서, 그보다 깊이 들어간 뒤에는 다시 올라오지 못하고 항모 밑으로 빠졌다).
    // 이동 전 높이보다 위에 있는 면(주기된 기체 등)은 지나온 면이 아니므로 무시한다.
    void ConstrainToSurface(float yBefore = float.NaN)
    {
        float top = Mathf.Max(float.IsNaN(yBefore) ? transform.position.y : yBefore, transform.position.y) + 3f;
        var origin = transform.position; origin.y = top;
        int count = Physics.RaycastNonAlloc(
            origin, Vector3.down,
            _groundHits, top - transform.position.y + 4f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float bestY = float.MinValue;
        bool  found = false;
        for (int i = 0; i < count; i++)
        {
            if (_groundHits[i].transform.IsChildOf(transform)) continue;
            if (_groundHits[i].point.y > top - 2.5f) continue;
            if (_groundHits[i].point.y > bestY) { bestY = _groundHits[i].point.y; found = true; }
        }
        if (!found) return;

        float floor = bestY - 0.097f;
        if (transform.position.y < floor)
        {
            var p = transform.position; p.y = floor; transform.position = p;
            // 갑판에 닿은 채 기수가 아래를 향하면 수평으로 눌러 준다 (계속 갑판을 파고들며 미끄러지지 않게)
            Vector3 fwd = transform.forward;
            if (fwd.y < 0f)
            {
                Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.FromToRotation(fwd, flat.normalized) * transform.rotation;
            }
        }
    }

    // ── Reconciliation ─────────────────────────────────────────────────────────
    void HandleMoveAck(MoveAckPacket ack)
    {
        var serverPos = new Vector3(ack.posX, ack.posY, ack.posZ);

        float reconSpeed = currentSpeed;
        bool  found      = false;
        _reconScratch.Clear();

        while (_inputHistory.Count > 0)
        {
            var r = _inputHistory.Dequeue();
            if (r.tick == ack.tick)
            {
                float error = Vector3.Distance(r.posAfter, serverPos);
                if (error > Cfg.reconcileThreshold)
                {
                    reconSpeed = r.velAfter.magnitude;
                    found      = true;
                }
            }
            else if (r.tick > ack.tick)
            {
                _reconScratch.Enqueue(r);
            }
        }

        if (!found)
        {
            while (_reconScratch.Count > 0) _inputHistory.Enqueue(_reconScratch.Dequeue());
            return;
        }

        var   pos = serverPos;
        var   rot = Quaternion.Euler(ack.rotX, ack.rotY, ack.rotZ);
        float spd = reconSpeed;

        foreach (var r in _reconScratch)
        {
            if (r.throttle > 0.5f)
                spd = Mathf.MoveTowards(spd, Cfg.maxSpeed, Cfg.acceleration * r.dt);
            else if (r.throttle < -0.5f)
                spd = Mathf.MoveTowards(spd, 0f, Cfg.acceleration * r.dt);
            else
                spd = Mathf.MoveTowards(spd, 0f, Cfg.drag * r.dt);

            rot = rot * Quaternion.Euler(r.pitch, r.yaw, r.roll);
            pos += rot * Vector3.forward * spd * r.dt;

            _inputHistory.Enqueue(r);
        }
        _reconScratch.Clear();

        transform.SetPositionAndRotation(pos, rot);
        currentSpeed = spd;
    }

    // ── 원격 플레이어: 스냅샷 보간 ────────────────────────────────────────────
    void RemoteUpdate()
    {
        if (snapshots.Count == 0) return;

        if (snapshots.Count == 1)
        {
            transform.position = snapshots[0].position;
            transform.rotation = snapshots[0].rotation;
            return;
        }

        float renderTime = Time.time - Cfg.interpolationDelay;

        while (snapshots.Count > 2 && snapshots[1].time <= renderTime)
            snapshots.RemoveAt(0);

        Snapshot from = snapshots[0];
        Snapshot to   = snapshots[1];

        float t = to.time > from.time
            ? Mathf.Clamp01(Mathf.InverseLerp(from.time, to.time, renderTime))
            : 1f;

        transform.position = Vector3.Lerp(from.position, to.position, t);
        transform.rotation = Quaternion.Slerp(from.rotation, to.rotation, t);

        if (anim != null && _hasMoveParam) anim.SetBool(s_hashMove, to.isMove);
    }

    // ── 스냅샷 ────────────────────────────────────────────────────────────────
    public void AddSnapshot(Vector3 pos, Quaternion rot, bool isMove)
    {
        snapshots.Add(new Snapshot
            { position = pos, rotation = rot, isMove = isMove, time = Time.time });
        if (snapshots.Count > 12) snapshots.RemoveAt(0);
    }

    public void ClearSnapshots() => snapshots.Clear();

    // GroundController가 단일 틱 시퀀스를 공유하도록 노출 (현재 GroundController는 SocketClient.GetNextTick() 직접 사용)
    public int GetNextTick() => localTick = NetworkManager.Instance.socketClient.GetNextTick();

    // 비행 상태 전환 시 즉시 한 번 전송 (비행 종료 시 isFlying=false 알림용)
    public void SendFlightState(bool flying)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        Vector3 euler = transform.eulerAngles;
        sc.SendMove(
            transform.position.x, transform.position.y, transform.position.z,
            euler.x, euler.y, euler.z,
            currentSpeed > 0.5f, flying, localTick);
    }

    // 콕핏 탑승 상태 전환 — TCP로 전송해 서버 상태를 즉시·확실하게 갱신
    public void SendCockpitState(bool boarded)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.SendCockpitStateTCP(boarded, transform.position, transform.eulerAngles);
    }

    /// <summary>카타펄트 사출 등 외부에서 초기 속도를 설정.</summary>
    public void SetInitialSpeed(float speedMS) => currentSpeed = speedMS;

    // 자동 기동(배럴롤/코브라)에서 MOVE 패킷을 분산해 보내기 위한 헬퍼.
    // 일반 경로에서는 inline으로 보냄 — 중복 피하려 자동 기동 블록에서만 사용.
    void SendMoveTick(float dt, bool isMove)
    {
        moveSendTimer += dt;
        if (moveSendTimer < Cfg.sendInterval) return;
        moveSendTimer -= Cfg.sendInterval;
        localTick = NetworkManager.Instance.socketClient.GetNextTick();
        Vector3 euler = transform.eulerAngles;
        NetworkManager.Instance.socketClient.SendMove(
            transform.position.x, transform.position.y, transform.position.z,
            euler.x, euler.y, euler.z,
            isMove,
            GameModeManager.Instance != null && GameModeManager.Instance.IsFlying,
            localTick);
    }

    // Unity의 eulerAngles는 [0, 360] 범위 → [-180, 180]으로 정규화.
    static float NormalizeAngle(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        return deg;
    }
}
