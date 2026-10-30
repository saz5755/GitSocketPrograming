using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 비행 교관 코치 — 두 가지 역할:
///   1) 실전 훈련 (비행 교관 NPC / 가이드에서 시작): 단계마다 화면 중앙에 "무엇을·왜" 소개 카드를 띄우고,
///      하단 안내창에 누를 키(누르는 동안 불이 켜짐)·진행 막대·실시간 상황(적기 방위 등)을 보여 주며
///      플레이어의 실제 조작(탑승·시동·발진·애프터버너·무장 선택·락온·발사·플레어·미사일 회피)을 감지해 다음 단계로 넘어간다.
///   2) 위협 코칭 (COMBAT 방, 설정으로 끌 수 있음): RWR 단계가 올라가는 순간 "지금 무엇을 해야 하는지" 를 화면 중앙 위에 알린다.
/// GameModeManager 가 런타임에 생성한다 (씬 배치 불필요).
/// </summary>
public class FlightCoach : MonoBehaviour
{
    public static FlightCoach Instance { get; private set; }

    const string PrefTips = "guide.threatTips";
    public static bool ThreatTipsEnabled
    {
        get => PlayerPrefs.GetInt(PrefTips, 1) != 0;
        set { PlayerPrefs.SetInt(PrefTips, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static bool TrainingActive => Instance != null && Instance._training;

    public static void StartTraining() { Ensure(); Instance.BeginTraining(); }
    public static void StopTraining()  { if (Instance != null) Instance.EndTraining(); }

    static void Ensure() { if (Instance == null) new GameObject("FlightCoach").AddComponent<FlightCoach>(); }

    /// <summary>하단 훈련 안내 막대가 떠 있는가 — 같은 자리를 쓰는 AAR 패널이 위로 비켜 선다</summary>
    public static bool BarShown => Instance != null && Instance._bar != null && Instance._bar.activeSelf;

    const KeyCode SkipKey = KeyCode.F2;
    const KeyCode QuitKey = KeyCode.F3;

    // ── 훈련 단계 ─────────────────────────────────────────────────────────
    enum Step { Board, Preflight, Launch, Afterburner, Roll, WeaponSelect, Lock, Fire, Gun, Flare, Evade, TankerRendezvous, AirRefuel }

    /// <summary>안내창에 그릴 키캡 — 누르고 있는 동안 불이 켜진다</summary>
    struct Cap { public string label, caption; public Func<bool> held; }

    class StepInfo
    {
        public Step   id;
        public string title, hint, desc;
        public bool   combatOnly;
        public Cap[]  caps;
    }

    StepInfo[] _steps;
    int   _cur;
    bool  _training, _finished;
    float _doneTimer;
    float _completeHold;     // 단계 완료 연출 중 (0 이 되면 다음 단계)

    // 감지용 이전 값
    int  _prevMsl = -1, _prevAmmo = -1, _prevFlare = -1;
    MissileLauncher.Weapon _prevWeapon;
    bool _sawMissile;
    float _rollHeld;

    // ── UI ────────────────────────────────────────────────────────────────
    Font _font;
    Text _tip;
    float _tipTimer;
    ThreatWarningSystem.ThreatLevel _prevThreat;
    float _nextTipAllowed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    static Cap Key(SimAction a, string caption) => new Cap { label = PilotGuideUI.K(a), caption = caption, held = () => SimInput.Held(a) };
    static Cap Raw(string label, string caption, Func<bool> held) => new Cap { label = label, caption = caption, held = held };
    static Cap Lmb(string caption) => Raw("좌클릭", caption, () => Input.GetMouseButton(0));
    static Cap Mouse(string caption) => Raw("마우스", caption, () => Mathf.Abs(Input.GetAxis("Mouse X")) + Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.05f);

    void BuildSteps()
    {
        string K(SimAction a) => PilotGuideUI.K(a);
        Cap confirm = Raw(PilotGuideUI.KeyName(SimInput.ConfirmKey), "상호작용", () => Input.GetKey(SimInput.ConfirmKey));
        _steps = new[]
        {
            new StepInfo { id = Step.Board, title = "전투기 탑승",
                hint = "갑판의 전투기 옆으로 걸어가 [F]",
                desc = "WASD 로 걸어서 갑판에 세워진 전투기 옆에 서면 탑승 안내가 뜹니다.",
                caps = new[] { Raw("WASD", "이동", () => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)), confirm } },
            new StepInfo { id = Step.Preflight, title = "시동 절차",
                hint = "콕핏 스위치를 순서대로 좌클릭: APU → ENG → AV",
                desc = "실기처럼 보조동력(APU)으로 엔진을 돌리고 항전 장비를 켭니다. 표시등이 켜질 때까지 기다렸다 다음 스위치를 누르세요. 그동안 갑판 요원이 연료를 가득 채워 줍니다.",
                caps = new[] { Lmb("스위치") } },
            new StepInfo { id = Step.Launch, title = "카타펄트 발진",
                hint = $"[F] 로 발진 → 이륙 직후 [{K(SimAction.ThrottleUp)}] 홀드로 추력 유지",
                desc = "카타펄트가 기체를 순식간에 이륙 속도까지 밀어 줍니다. 갑판을 벗어나면 추력을 올려 속도를 지키세요.",
                caps = new[] { confirm, Key(SimAction.ThrottleUp, "추력 ↑") } },
            new StepInfo { id = Step.Afterburner, title = "애프터버너",
                hint = $"[{K(SimAction.ThrottleUp)}] + [{K(SimAction.Afterburner)}] 함께 홀드",
                desc = "최대 추력. 미사일을 피하거나 적과 거리를 벌릴 때 쓰지만 연료를 빠르게 소모하고 적외선 신호가 커집니다.",
                caps = new[] { Key(SimAction.ThrottleUp, "추력 ↑"), Raw(K(SimAction.Afterburner), "A/B", () => SimInput.Afterburner) } },
            new StepInfo { id = Step.Roll, title = "선회 기동",
                hint = $"[{K(SimAction.RollLeft)}]/[{K(SimAction.RollRight)}] 로 기울이고 마우스를 당겨 선회",
                desc = "전투기는 날개를 기울인 뒤 기수를 당겨서 돕니다. 막대가 찰 때까지 1초 정도 기울여 보세요.",
                caps = new[] { Key(SimAction.RollLeft, "좌 롤"), Key(SimAction.RollRight, "우 롤"), Mouse("기수") } },
            new StepInfo { id = Step.WeaponSelect, title = "무장 선택", combatOnly = true,
                hint = $"[{K(SimAction.WeaponSelect)}] 로 AIM-9X ↔ AIM-120 전환",
                desc = "AIM-9X: 근거리 열추적(락 톤 후 발사)   AIM-120: 원거리 레이더 유도(락온 후 발사). 우측 하단 무장 카드에서 확인하세요.",
                caps = new[] { Key(SimAction.WeaponSelect, "무장") } },
            new StepInfo { id = Step.Lock, title = "레이더 락온", combatOnly = true,
                hint = $"적기를 기수 앞(화면 안)에 두고 [{K(SimAction.LockTarget)}] — 약 2.5초",
                desc = "화면 가장자리의 빨간 화살표가 적기 방향입니다. 그쪽으로 선회해 적기가 화면 안에 들어오면 락온하세요.",
                caps = new[] { Key(SimAction.LockTarget, "락온"), Mouse("조준") } },
            new StepInfo { id = Step.Fire, title = "미사일 발사", combatOnly = true,
                hint = $"HUD 큐가 IN RNG / SHOOT 일 때 [{K(SimAction.FireMissile)}]",
                desc = $"멀면(3.5km 이상) [{K(SimAction.WeaponSelect)}] 로 AIM-120 을 고르고 락온 후 발사. AIM-9X 는 가까이서 시커 락 톤(높은 음)이 들린 뒤에 쏩니다.",
                caps = new[] { Key(SimAction.FireMissile, "발사") } },
            new StepInfo { id = Step.Gun, title = "기총 사격", combatOnly = true,
                hint = "마우스 좌클릭 홀드 — 리드 마커에 맞춰 짧게 끊어 쏘기",
                desc = "750 m 안쪽 근접전 무기. 적기 진행 방향 앞(리드 마커)을 겨눠야 맞습니다.",
                caps = new[] { Lmb("기총") } },
            new StepInfo { id = Step.Flare, title = "플레어 투하",
                hint = $"[{K(SimAction.Flare)}] 플레어 · [{K(SimAction.Chaff)}] 채프",
                desc = "플레어는 열추적 미사일, 채프는 레이더 미사일을 속입니다. 한 번 눌러 투하해 보세요.",
                caps = new[] { Key(SimAction.Flare, "플레어"), Key(SimAction.Chaff, "채프") } },
            new StepInfo { id = Step.Evade, title = "미사일 회피", combatOnly = true,
                hint = "MISSILE 경보 → 미사일 쪽 측면(90°)으로 급선회 + 플레어/채프",
                desc = "적기에 다가가면 적이 미사일을 쏩니다. 경보가 울리면 RWR 의 미사일 방향을 옆에 두고 급선회하며 기만체를 뿌리세요. 살아남으면 완료.",
                caps = new[] { Key(SimAction.Flare, "플레어"), Key(SimAction.Chaff, "채프"), Mouse("급선회") } },
            new StepInfo { id = Step.TankerRendezvous, title = "급유기 랑데부",
                hint = "HUD 의 TANKER 마커를 따라 급유기 1.2km 안으로",
                desc = "연료가 떨어지면 하늘에서 급유받습니다. 급유기는 항모 북동쪽 고도 약 1,500m 를 천천히 돌고 있습니다. 가까이 가면 급유 프로브가 자동으로 펴집니다.",
                caps = new[] { Key(SimAction.ThrottleUp, "추력"), Mouse("방향") } },
            new StepInfo { id = Step.AirRefuel, title = "공중급유 접속",
                hint = "바스켓 뒤에서 속도를 맞추고 1~3 m/s 로 천천히 꽂기",
                desc = "급유기 뒤 아래 늘어진 바스켓 뒤에 자리 잡고, 하단 AAR 패널의 ▲▼◀▶ 로 정렬하세요. 천천히 밀어 넣어 CONTACT 가 되면 같은 속도로 자세를 유지합니다. 너무 빠르면 튕겨 나옵니다.",
                caps = new[] { Key(SimAction.ThrottleUp, "가속"), Key(SimAction.ThrottleDown, "감속"), Mouse("정렬") } },
        };
        if (RoomModeSO.IsAirshow)
            _steps = Array.FindAll(_steps, s => !s.combatOnly);
    }

    void BeginTraining()
    {
        BuildSteps();
        _training = true; _finished = false; _cur = 0; _doneTimer = 0f; _completeHold = 0f;
        ResetDetect();
        SkipSatisfied();
        BuildDots();
        EnterStep();
    }

    void EndTraining()
    {
        _training = false;
        _bar.SetActive(false);
        _intro.gameObject.SetActive(false);
    }

    void ResetDetect() { _prevMsl = _prevAmmo = _prevFlare = -1; _sawMissile = false; _rollHeld = 0f; _contactHeld = 0f; }
    float _contactHeld;

    // 이미 끝낸 단계 건너뛰기 (예: 비행 중 훈련 시작)
    void SkipSatisfied()
    {
        var gm = GameModeManager.Instance;
        if (gm == null) return;
        while (_cur < _steps.Length)
        {
            var id = _steps[_cur].id;
            bool done = id switch
            {
                Step.Board     => gm.IsBoardedInCockpit || gm.IsFlying,
                Step.Preflight => gm.IsFlying || (PreflightSystem.Instance != null && PreflightSystem.Instance.IsReadyForLaunch),
                Step.Launch    => gm.IsFlying,
                _              => false,
            };
            if (!done) break;
            _cur++;
        }
    }

    // 새 단계 진입 — 소개 카드 + 안내창 갱신
    void EnterStep()
    {
        ResetDetect();
        if (_cur >= _steps.Length) { Finish(); return; }
        var s = _steps[_cur];
        var l = FindCached(ref _launcher);
        if (l != null) _prevWeapon = l.Selected;

        _barBadgeNum.text = (_cur + 1).ToString();
        _barTitle.text    = s.title;
        _barHint.text     = s.hint;
        _barStep.text     = $"STEP {_cur + 1} / {_steps.Length}";
        SetCaps(_barCaps, s.caps);
        RefreshDots();

        ShowIntro($"STEP {_cur + 1} / {_steps.Length}", s.title, s.desc, s.caps, 4.5f);
    }

    void Finish()
    {
        _finished = true;
        _doneTimer = 0f;
        _bar.SetActive(false);
        ShowIntro("TRAINING COMPLETE", "훈련 완료!",
            RoomModeSO.IsAirshow ? "기본 조작을 모두 익혔습니다. 편대 비행에 도전해 보세요."
                                 : "실전에서도 RWR 경보에 주의하세요. 위협 코칭은 ESC ▸ PILOT GUIDE 에서 켜고 끌 수 있습니다.",
            null, 5f);
    }

    // ── Update ────────────────────────────────────────────────────────────
    void Update()
    {
        if (_training) UpdateTraining();
        UpdateThreatTips();
        if (_tipTimer > 0f && (_tipTimer -= Time.deltaTime) <= 0f) _tip.gameObject.SetActive(false);

        bool hide = PilotGuideUI.IsOpen;
        bool showBar = _training && !_finished && !hide;
        if (_bar.activeSelf != showBar) _bar.SetActive(showBar);
        _canvas.enabled = !hide || _tip.gameObject.activeSelf;
        UpdateIntro();
    }

    void UpdateTraining()
    {
        if (_finished)
        {
            if ((_doneTimer += Time.deltaTime) > 6f) EndTraining();
            return;
        }
        if (PilotGuideUI.IsOpen) return;

        if (Input.GetKeyDown(QuitKey)) { EndTraining(); ShowTip("훈련을 종료했습니다 — 교관 NPC 나 ESC ▸ PILOT GUIDE 에서 다시 시작", Ink, 3f); return; }

        var s = _steps[_cur];
        UpdateCaps(_barCaps, s.caps);
        _pulse += Time.deltaTime;

        // 완료 연출 중
        if (_completeHold > 0f)
        {
            _completeHold -= Time.deltaTime;
            SetBarAccent(Good);
            SetProgress(1f);
            _barProgressRoot.SetActive(true);
            if (_completeHold <= 0f) { _cur++; EnterStep(); }
            return;
        }
        SetBarAccent(Accent);

        if (Input.GetKeyDown(SkipKey)) { Complete(skipped: true); return; }

        bool done = Detect(s.id, out float progress, out string live);
        _barLive.text = live ?? "";
        _barProgressRoot.SetActive(progress >= 0f);
        if (progress >= 0f) SetProgress(progress);
        if (done) Complete(skipped: false);
    }

    void Complete(bool skipped)
    {
        _completeHold = 0.9f;
        _barBadgeNum.text = "✔";
        _barLive.text = skipped ? "건너뜀" : "완료!";
        if (!skipped) ShowTip($"✔ {_steps[_cur].title} 완료", Good, 2f);
        RefreshDots(_cur);
    }

    /// <summary>현재 단계 달성 여부 + 진행률(0..1, 측정 불가면 -1) + 실시간 상황 문구</summary>
    bool Detect(Step id, out float progress, out string live)
    {
        progress = -1f; live = null;
        var gm       = GameModeManager.Instance;
        var launcher = id is Step.WeaponSelect or Step.Lock or Step.Fire ? FindCached(ref _launcher) : null;
        var me       = LocalAircraft();
        var tw       = ThreatWarningSystem.Instance;
        bool flying  = gm != null && gm.IsFlying;

        switch (id)
        {
            case Step.Board:
                live = gm != null && gm.HasBoardingTarget ? "탑승 가능 — 지금 [F]" : "갑판 위 전투기로 이동하세요";
                return gm != null && (gm.IsBoardedInCockpit || flying);

            case Step.Preflight:
            {
                var pf = PreflightSystem.Instance;
                if (pf != null)
                {
                    progress = (float)pf.Stage / (float)PreflightStage.ReadyForLaunch;
                    live = pf.Stage switch
                    {
                        PreflightStage.None or PreflightStage.Waiting => "APU 스위치부터 클릭",
                        PreflightStage.ApuStarting    => "APU 가동 중… 잠시 기다리세요",
                        PreflightStage.ApuReady       => "다음: ENG 스위치",
                        PreflightStage.EngineStarting => "엔진 점화 중…",
                        PreflightStage.EngineIdle     => "다음: AV(항전) 스위치",
                        PreflightStage.AvionicsOn     => "항전 장비 기동 중…",
                        _                             => "출격 준비 완료",
                    };
                }
                return flying || (pf != null && pf.IsReadyForLaunch);
            }

            case Step.Launch:
                live = "[F] 를 눌러 카타펄트 발진";
                return flying;

            case Step.Afterburner:
                live = me != null && me.AfterburnerActive ? "애프터버너 점화!" : "두 키를 동시에 누르고 있으세요";
                return me != null && me.AfterburnerActive;

            case Step.Roll:
                if (me != null && Mathf.Abs(SimInput.Roll) > 0.5f) _rollHeld += Time.deltaTime;
                progress = _rollHeld / 1.0f;
                return _rollHeld > 1.0f;

            case Step.WeaponSelect:
                if (launcher != null) live = $"현재 무장: {(launcher.Selected == MissileLauncher.Weapon.AIM9X ? "AIM-9X (열추적)" : "AIM-120 (레이더)")}";
                return launcher != null && launcher.Selected != _prevWeapon;

            case Step.Lock:
            {
                var ts = FindCached(ref _targeting);
                bool locked = ts != null && ts.State == TargetingSystem.LockState.Locked;
                locked |= launcher != null && launcher.Seeker == MissileLauncher.SeekerState.Locked;   // AIM-9X 시커 락도 인정
                if (ts != null && ts.Target != null) { progress = ts.LockProgress; live = $"락온 중… {ts.Target.nickname}  {Km(ts.TargetRange)}"; }
                else live = EnemyPointer(me, "그쪽으로 선회해 화면 안에 넣으세요");
                return locked;
            }

            case Step.Fire:
            {
                int m = launcher != null ? launcher.Aim9Count + launcher.Aim120Count : -1;
                if (_prevMsl < 0) _prevMsl = m;
                if (launcher != null)
                {
                    string cue = launcher.Cue(out bool urgent);
                    live = cue switch
                    {
                        "SHOOT" or "IN RNG" => $"HUD 큐 {cue} — 지금 발사!",
                        "OUT RNG"           => $"HUD 큐 OUT RNG — 더 접근하세요 ({Km(launcher.Range)} / 사거리 {Km(launcher.RMax)})",
                        "MIN RNG"           => "HUD 큐 MIN RNG — 너무 가깝습니다. 거리를 벌리세요",
                        "EMPTY"             => $"미사일 소진 — [{PilotGuideUI.K(SimAction.WeaponSelect)}] 로 다른 무장 선택",
                        _                   => launcher.DenyReason() is string why && (_targeting != null && _targeting.Target != null || launcher.SeekerTarget != null)
                                               ? why : EnemyPointer(me, $"HUD 큐 {cue} — 적기를 기수 앞에 두세요"),
                    };
                }
                return m >= 0 && m < _prevMsl;
            }

            case Step.Gun:
            {
                var gun = FindCached(ref _gun);
                int a = gun != null ? gun.Ammo : -1;
                if (_prevAmmo < 0) _prevAmmo = a;
                if (a >= 0) progress = (_prevAmmo - a) / 6f;
                return a >= 0 && a < _prevAmmo - 5;
            }

            case Step.Flare:
            {
                var cms = FindCached(ref _cms);
                int f = cms != null ? cms.FlareRemaining + cms.ChaffRemaining : -1;
                if (_prevFlare < 0) _prevFlare = f;
                if (cms != null) live = $"남은 플레어 {cms.FlareRemaining} · 채프 {cms.ChaffRemaining}";
                return f >= 0 && f < _prevFlare;
            }

            case Step.TankerRendezvous:
            {
                var ars = AerialRefuelSystem.Instance;
                if (!flying) { live = "비행 중에만 급유기를 찾을 수 있습니다"; return false; }
                if (ars == null || !ars.HasTanker) { live = "급유기 투입 대기 중"; return false; }
                float d = ars.TankerDistance, dy = ars.TankerAltDiff;
                string alt = Mathf.Abs(dy) < 100f ? "같은 고도" : dy > 0f ? $"위 {dy:F0}m" : $"아래 {-dy:F0}m";
                live = $"급유기: 방위 {ars.TankerBearing:000}° · {Km(d)} · {alt}";
                progress = Mathf.Clamp01(1f - (d - AerialRefuelSystem.ProbeRange) / 8000f);
                return d < AerialRefuelSystem.ProbeRange;
            }

            case Step.AirRefuel:
            {
                var ars = AerialRefuelSystem.Instance;
                if (ars == null || !flying) { live = "비행 중에만 가능합니다"; return false; }
                if (ars.InContact)
                {
                    _contactHeld += Time.deltaTime;
                    live = me != null ? $"CONTACT — 연료 {me.CurrentFuel / me.MaxFuel * 100f:F0}%  · 자세 유지!" : "CONTACT";
                    progress = _contactHeld / 2f;
                    return _contactHeld >= 2f;
                }
                _contactHeld = 0f;
                if (ars.TankerDistance > AerialRefuelSystem.ProbeRange) live = $"급유기에서 멀어졌습니다 — {Km(ars.TankerDistance)}";
                else if (!ars.ProbeReady) live = "급유 프로브 전개 중…";
                else
                {
                    float c = ars.Closure;
                    string pace = c > AerialRefuelSystem.MaxClosure ? "너무 빠름 — 감속!" : c < 0.3f ? "조금 더 다가가세요" : "좋습니다 — 그대로";
                    live = $"바스켓 {ars.DrogueDistance:F1}m · 접근 {c:+0.0;-0.0} m/s — {pace}";
                }
                return false;
            }

            case Step.Evade:
                if (tw != null && tw.MissileIncoming) _sawMissile = true;
                if (tw != null && tw.MissileIncoming)
                    live = $"MISSILE {Clock(tw.ThreatBearing)} 방향 {Km(tw.NearestMissileDist)} — 측면 급선회 + 기만체!";
                else if (_sawMissile) live = "회피 성공 확인 중…";
                else live = EnemyPointer(me, "적기에 접근하면 미사일을 쏩니다 — 경보를 기다리세요");
                return _sawMissile && tw != null && !tw.MissileIncoming && flying;
        }
        return false;
    }

    // 가장 가까운 적기 방위·거리 안내 (뒤에 붙은 적기도 알 수 있게)
    static string EnemyPointer(PlayerController me, string whenFront)
    {
        if (me == null) return "비행 중에만 적기를 찾을 수 있습니다";
        PlayerController best = null; float bestD = float.MaxValue;
        foreach (var pc in PlayerController.All)
        {
            if (!TargetingSystem.IsHostile(pc)) continue;
            float d = Vector3.Distance(pc.transform.position, me.transform.position);
            if (d < bestD) { bestD = d; best = pc; }
        }
        if (best == null) return "적기 없음 — 적기 투입을 기다리는 중";

        Vector3 to  = best.transform.position - me.transform.position;
        float   off = Vector3.Angle(me.transform.forward, to);
        Vector3 f   = me.transform.forward; f.y = 0f;
        Vector3 t   = to; t.y = 0f;
        string  clk = Clock(Vector3.SignedAngle(f, t, Vector3.up));
        float   dy  = to.y;
        string  alt = Mathf.Abs(dy) < 150f ? "같은 고도" : dy > 0f ? $"위 {dy:F0}m" : $"아래 {-dy:F0}m";
        string  where = $"적기 {best.nickname}: {clk} 방향 {Km(bestD)} · {alt}";
        return off < 25f ? $"{where} — {whenFront}" : $"{where} — 빨간 화살표 쪽으로 선회";
    }

    static string Km(float m) => m >= 1000f ? $"{m / 1000f:F1}km" : $"{m:F0}m";

    // ── 위협 코칭 ─────────────────────────────────────────────────────────
    void UpdateThreatTips()
    {
        var tw = ThreatWarningSystem.Instance;
        var gm = GameModeManager.Instance;
        bool active = tw != null && gm != null && gm.IsFlying && !RoomModeSO.IsAirshow && (ThreatTipsEnabled || _training);
        if (!active) { _prevThreat = ThreatWarningSystem.ThreatLevel.None; return; }

        var lv = tw.MissileIncoming && tw.Threat < ThreatWarningSystem.ThreatLevel.MissileFired
               ? ThreatWarningSystem.ThreatLevel.MissileFired : tw.Threat;
        bool escalated = lv > _prevThreat;
        _prevThreat = lv;
        if (!escalated || (Time.time < _nextTipAllowed && lv < ThreatWarningSystem.ThreatLevel.MissileFired)) return;

        string f = PilotGuideUI.K(SimAction.Flare), c = PilotGuideUI.K(SimAction.Chaff);
        string dir = Clock(tw.ThreatBearing);
        switch (lv)
        {
            case ThreatWarningSystem.ThreatLevel.Tracked:
                ShowTip($"적 레이더가 당신을 추적 중 ({dir} 방향) — 기수를 돌려 맞서거나 거리를 벌리세요", Warn, 3.5f); break;
            case ThreatWarningSystem.ThreatLevel.Locked:
                ShowTip($"⚠ 락온 당했습니다! ({dir}) — 곧 미사일이 옵니다. 측면 90° 선회 준비, 채프 [{c}]", Warn, 4f); break;
            case ThreatWarningSystem.ThreatLevel.MissileFired:
                ShowTip($"MISSILE! ({dir}) — 미사일 방향의 측면으로 급선회 + 플레어 [{f}] 연타 (레이더면 채프 [{c}])", Crit, 4.5f); break;
            case ThreatWarningSystem.ThreatLevel.MissileActive:
                ShowTip($"레이더 미사일 종말 유도! — 측면 비행(노치) + 채프 [{c}], 마지막 순간 급선회", Crit, 4.5f); break;
            default: return;
        }
        _nextTipAllowed = Time.time + 6f;
    }

    // 방위각(°, 0 = 정면, + = 우) → 시계 방향 표기
    static string Clock(float bearing)
    {
        int h = Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 30f) % 12;
        return $"{(h == 0 ? 12 : h)}시";
    }

    // ── 참조 ──────────────────────────────────────────────────────────────
    MissileLauncher _launcher; GunSystem _gun; CountermeasureSystem _cms; TargetingSystem _targeting;
    // AI 기체(플레이어 프리팹 기반)에 붙은 비활성 컴포넌트는 건너뛴다
    static float s_nextFind;
    static T FindCached<T>(ref T field) where T : Behaviour
    {
        if (field != null && field.isActiveAndEnabled) return field;
        field = null;
        if (Time.unscaledTime < s_nextFind) return null;   // 못 찾으면 0.5초마다만 다시 검색
        foreach (var c in FindObjectsByType<T>())
            if (c.isActiveAndEnabled) { field = c; break; }
        if (field == null) s_nextFind = Time.unscaledTime + 0.5f;
        return field;
    }

    static PlayerController LocalAircraft()
    {
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) return pc;
        return null;
    }

    // ── UI ────────────────────────────────────────────────────────────────
    static readonly Color Ink    = new Color(0.90f, 1.00f, 0.97f, 0.96f);
    static readonly Color Dim    = new Color(0.62f, 0.78f, 0.82f, 0.80f);
    static readonly Color Accent = new Color(0.27f, 0.84f, 0.95f, 1f);      // 교관(원자 로고) 청록
    static readonly Color Glass  = new Color(0.02f, 0.06f, 0.10f, 0.82f);
    static readonly Color Good   = new Color(0.20f, 1.00f, 0.55f, 1f);
    static readonly Color Warn   = new Color(1.00f, 0.72f, 0.25f, 1f);
    static readonly Color Crit   = new Color(1.00f, 0.36f, 0.40f, 1f);

    class CapView { public GameObject go; public Image bg; public Outline line; public Text label, caption; }

    Canvas _canvas;
    // 하단 안내창
    GameObject _bar;
    Image _barBadge; Outline _barOutline;
    Text _barBadgeNum, _barTitle, _barHint, _barLive, _barStep;
    GameObject _barProgressRoot; Image _barProgress;
    RectTransform _dotsRoot; Image[] _dots = Array.Empty<Image>();
    CapView[] _barCaps;
    float _pulse;
    // 중앙 소개 카드
    CanvasGroup _intro; RectTransform _introRt;
    Text _introStep, _introTitle, _introDesc, _introFoot;
    CapView[] _introCaps;
    Cap[] _introCapData;
    float _introT, _introHold, _introHeldTime;

    void ShowTip(string msg, Color c, float seconds)
    {
        _tip.text = msg; _tip.color = c;
        _tip.gameObject.SetActive(true);
        _tipTimer = seconds;
    }

    void ShowIntro(string step, string title, string desc, Cap[] caps, float hold)
    {
        _introStep.text  = step;
        _introTitle.text = title;
        _introDesc.text  = desc;
        _introCapData    = caps;
        SetCaps(_introCaps, caps);
        _introFoot.text  = caps != null ? "직접 해 보세요 — 진행 상황은 아래 안내창에 표시됩니다" : "";
        _introT = 0f; _introHold = hold; _introHeldTime = 0f;
        _intro.gameObject.SetActive(true);
    }

    // 페이드 인 → 유지(플레이어가 해당 키를 누르기 시작하면 빨리 닫힘) → 페이드 아웃
    void UpdateIntro()
    {
        if (!_intro.gameObject.activeSelf) return;
        float dt = Time.unscaledDeltaTime;
        _introT += dt;
        if (_introCapData != null)
        {
            UpdateCaps(_introCaps, _introCapData);
            bool any = false;
            foreach (var c in _introCapData) any |= c.held != null && c.held();
            if (any && _introT > 0.6f) _introHeldTime += dt;
            if (_introHeldTime > 0.25f) _introHold = Mathf.Min(_introHold, _introT);
        }

        const float fadeIn = 0.25f, fadeOut = 0.35f;
        float a = _introT < fadeIn ? _introT / fadeIn
                : _introT < _introHold ? 1f
                : 1f - (_introT - _introHold) / fadeOut;
        if (a <= 0f && _introT > _introHold) { _intro.gameObject.SetActive(false); return; }
        _intro.alpha = Mathf.Clamp01(a) * (PilotGuideUI.IsOpen ? 0f : 1f);
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_introT / fadeIn));
        _introRt.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, k);
    }

    void SetProgress(float p) => _barProgress.rectTransform.sizeDelta = new Vector2(780f * Mathf.Clamp01(p), 5f);

    void SetBarAccent(Color c)
    {
        float p = 0.75f + 0.25f * Mathf.Sin(_pulse * 4f);
        _barBadge.color = new Color(c.r, c.g, c.b, 0.85f * p);
        _barOutline.effectColor = new Color(c.r, c.g, c.b, 0.55f);
        _barProgress.color = c;
    }

    void BuildDots()
    {
        foreach (var d in _dots) if (d != null) Destroy(d.gameObject);
        int n = _steps.Length;
        _dots = new Image[n];
        const float gap = 22f;
        for (int i = 0; i < n; i++)
        {
            var rt = Rect("Dot", _dotsRoot, new Vector2((i - (n - 1) * 0.5f) * gap, 0f), new Vector2(10f, 10f));
            _dots[i] = rt.gameObject.AddComponent<Image>();
            _dots[i].sprite = HudSprites.Get(HudSprites.Disc);
            _dots[i].raycastTarget = false;
        }
    }

    void RefreshDots(int justDone = -1)
    {
        for (int i = 0; i < _dots.Length; i++)
        {
            bool done = i < _cur || i == justDone;
            _dots[i].color = done ? Good : i == _cur ? Ink : new Color(Dim.r, Dim.g, Dim.b, 0.35f);
            _dots[i].rectTransform.sizeDelta = i == _cur && !done ? new Vector2(14f, 14f) : new Vector2(10f, 10f);
        }
    }

    void SetCaps(CapView[] views, Cap[] caps)
    {
        int n = caps?.Length ?? 0;
        float w = views[0].go.GetComponent<RectTransform>().sizeDelta.x, gap = w * 0.16f;
        bool right = views == _barCaps;   // 안내창은 오른쪽 정렬, 소개 카드는 가운데 정렬
        for (int i = 0; i < views.Length; i++)
        {
            bool on = i < n;
            views[i].go.SetActive(on);
            if (!on) continue;
            views[i].label.text   = caps[i].label;
            views[i].caption.text = caps[i].caption;
            // 한글은 글자 폭이 넓어 2칸으로 센다
            int units = 0; foreach (char ch in caps[i].label) units += ch > 0x7F ? 2 : 1;
            views[i].label.fontSize = Mathf.RoundToInt(w * (units > 4 ? 0.21f : units > 3 ? 0.26f : 0.32f));
            float x = right ? -(n - 1 - i) * (w + gap) : (i - (n - 1) * 0.5f) * (w + gap);
            ((RectTransform)views[i].go.transform).anchoredPosition = new Vector2(x, 0f);
        }
    }

    static void UpdateCaps(CapView[] views, Cap[] caps)
    {
        if (caps == null) return;
        for (int i = 0; i < caps.Length && i < views.Length; i++)
            StyleCap(views[i], caps[i].held != null && caps[i].held());
    }

    static void StyleCap(CapView v, bool held)
    {
        v.bg.color         = held ? new Color(Accent.r, Accent.g, Accent.b, 0.92f) : new Color(0.05f, 0.12f, 0.17f, 0.95f);
        v.line.effectColor = held ? Color.white : new Color(Accent.r, Accent.g, Accent.b, 0.75f);
        v.label.color      = held ? new Color(0.02f, 0.06f, 0.10f, 1f) : Ink;
        v.go.transform.localScale = Vector3.one * (held ? 0.94f : 1f);   // 눌린 느낌
    }

    void Build()
    {
        var root = new GameObject("FlightCoach_Canvas");
        root.transform.SetParent(transform, false);
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 30;
        var cs = root.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        BuildBar(root.transform);
        BuildIntro(root.transform);

        // 중앙 상단 코칭 문구 (위협 경고 배너 아래)
        _tip = Txt(root.transform, "", new Vector2(0f, 330f), new Vector2(1200f, 30f), 20, Warn, TextAnchor.MiddleCenter, FontStyle.Bold);
        var sh = _tip.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.8f); sh.effectDistance = new Vector2(1.5f, -1.5f);
        _tip.gameObject.SetActive(false);
    }

    // 하단 중앙 안내창 (속도 테이프·시스템 카드·무장 카드 사이의 빈 공간)
    void BuildBar(Transform parent)
    {
        var bar = Panel("GuideBar", parent, new Vector2(0f, -318f), new Vector2(820f, 156f), out _barOutline);
        _bar = bar.gameObject;

        Txt(bar, "◆ 비행 교관  TRAINING", new Vector2(-394f, 60f), new Vector2(260f, 18f), 12, Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
        _barStep = Txt(bar, "", new Vector2(394f, 60f), new Vector2(160f, 18f), 12, Dim, TextAnchor.MiddleRight, FontStyle.Bold);
        _dotsRoot = Rect("Dots", bar, new Vector2(0f, 60f), new Vector2(300f, 16f));

        // 단계 번호 배지
        var badge = Rect("Badge", bar, new Vector2(-354f, -8f), new Vector2(64f, 64f));
        _barBadge = badge.gameObject.AddComponent<Image>();
        _barBadge.sprite = HudSprites.Get(HudSprites.Disc); _barBadge.raycastTarget = false;
        var ring = Rect("Ring", badge, Vector2.zero, new Vector2(76f, 76f)).gameObject.AddComponent<Image>();
        ring.sprite = HudSprites.Get(HudSprites.RingThin); ring.color = new Color(Accent.r, Accent.g, Accent.b, 0.6f); ring.raycastTarget = false;
        _barBadgeNum = Txt(badge, "1", Vector2.zero, new Vector2(64f, 64f), 28, new Color(0.02f, 0.06f, 0.10f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);

        _barTitle = Txt(bar, "", new Vector2(-306f, 24f), new Vector2(420f, 28f), 22, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        _barHint  = Txt(bar, "", new Vector2(-306f, -6f), new Vector2(420f, 22f), 15, Warn, TextAnchor.MiddleLeft);
        _barLive  = Txt(bar, "", new Vector2(-306f, -34f), new Vector2(500f, 20f), 14, Accent, TextAnchor.MiddleLeft);

        // 키캡 (오른쪽 정렬)
        var caps = Rect("Caps", bar, new Vector2(366f, 2f), new Vector2(10f, 10f));
        _barCaps = new CapView[4];
        for (int i = 0; i < _barCaps.Length; i++) _barCaps[i] = MakeCap(caps, 66f, 46f);

        // 진행 막대
        var pr = Rect("Progress", bar, new Vector2(0f, -64f), new Vector2(780f, 5f));
        pr.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.10f);
        var fill = Rect("Fill", pr, new Vector2(-390f, 0f), new Vector2(0f, 5f));
        fill.pivot = new Vector2(0f, 0.5f);
        _barProgress = fill.gameObject.AddComponent<Image>();   // 단색 — 너비로 진행률 표시
        _barProgress.raycastTarget = false;
        _barProgressRoot = pr.gameObject;

        Txt(bar, $"[{SkipKey}] 단계 건너뛰기    ·    [{QuitKey}] 훈련 종료", new Vector2(0f, -92f), new Vector2(600f, 18f), 12, Dim, TextAnchor.MiddleCenter);
        _bar.SetActive(false);
    }

    // 중앙 단계 소개 카드
    void BuildIntro(Transform parent)
    {
        var card = Panel("StepIntro", parent, new Vector2(0f, 168f), new Vector2(660f, 250f), out _);
        _introRt = card;
        _intro = card.gameObject.AddComponent<CanvasGroup>();
        _intro.blocksRaycasts = false; _intro.interactable = false;

        var stripe = Rect("Stripe", card, new Vector2(0f, 123f), new Vector2(660f, 4f)).gameObject.AddComponent<Image>();
        stripe.color = Accent; stripe.raycastTarget = false;

        _introStep  = Txt(card, "", new Vector2(0f, 96f), new Vector2(600f, 18f), 13, Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
        _introTitle = Txt(card, "", new Vector2(0f, 66f), new Vector2(600f, 40f), 32, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        _introDesc  = Txt(card, "", new Vector2(0f, 16f), new Vector2(600f, 56f), 16, Ink, TextAnchor.MiddleCenter);
        _introDesc.horizontalOverflow = HorizontalWrapMode.Wrap; _introDesc.lineSpacing = 1.1f;

        var caps = Rect("Caps", card, new Vector2(0f, -50f), new Vector2(10f, 10f));
        _introCaps = new CapView[4];
        for (int i = 0; i < _introCaps.Length; i++) _introCaps[i] = MakeCap(caps, 84f, 56f);

        _introFoot = Txt(card, "", new Vector2(0f, -108f), new Vector2(600f, 18f), 12, Dim, TextAnchor.MiddleCenter);
        card.gameObject.SetActive(false);
    }

    CapView MakeCap(Transform parent, float w, float h)
    {
        var rt = Rect("Cap", parent, Vector2.zero, new Vector2(w, h));
        var v = new CapView { go = rt.gameObject };
        v.bg = rt.gameObject.AddComponent<Image>();
        v.bg.sprite = HudSprites.Get(HudSprites.Panel); v.bg.type = Image.Type.Sliced; v.bg.raycastTarget = false;
        v.line = rt.gameObject.AddComponent<Outline>(); v.line.effectDistance = new Vector2(1.5f, 1.5f);
        v.label   = Txt(rt, "", Vector2.zero, new Vector2(w, h), Mathf.RoundToInt(w * 0.32f), Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        v.caption = Txt(rt, "", new Vector2(0f, -h * 0.5f - 11f), new Vector2(w + 20f, 16f), 12, Dim, TextAnchor.MiddleCenter);
        StyleCap(v, false);
        rt.gameObject.SetActive(false);
        return v;
    }

    RectTransform Panel(string n, Transform parent, Vector2 pos, Vector2 size, out Outline outline)
    {
        var rt = Rect(n, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = HudSprites.Get(HudSprites.Panel); img.type = Image.Type.Sliced;
        img.color = Glass; img.raycastTarget = false;
        outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.55f); outline.effectDistance = new Vector2(1.5f, 1.5f);
        return rt;
    }

    static RectTransform Rect(string n, Transform p, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(p, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Text Txt(Transform p, string s, Vector2 pos, Vector2 size, int fs, Color c, TextAnchor a, FontStyle st = FontStyle.Normal)
    {
        var rt = Rect("T", p, pos, size);
        if (a == TextAnchor.MiddleLeft)  rt.pivot = new Vector2(0f, 0.5f);
        if (a == TextAnchor.MiddleRight) rt.pivot = new Vector2(1f, 0.5f);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = _font; t.text = s; t.fontSize = fs; t.color = c; t.alignment = a; t.fontStyle = st;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
