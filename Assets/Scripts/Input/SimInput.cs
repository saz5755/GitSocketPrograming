using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

/// <summary>재지정 가능한 조작 동작. 순서를 바꾸면 저장된 바인딩이 어긋나므로 뒤에만 추가한다.</summary>
public enum SimAction
{
    ThrottleUp, ThrottleDown, Afterburner,
    RollLeft, RollRight,
    BarrelRollLeft, BarrelRollRight, Immelmann,
    FireMissile, LockTarget, Flare, Chaff,
    ViewToggle, TargetCam, MissileCam,
    WeaponSelect,
    SmokeToggle, SmokeColor, Formation,   // v4.4 에어쇼 (끝에 추가 — 저장된 키 인덱스 유지)
    CallTanker,                           // 급유기 호출
}

/// <summary>
/// 시뮬레이터 입력 추상화 — 조작 장치 매핑을 이 한 곳에 모은다.
/// 키보드/마우스 + (Input System) 조이스틱·HOTAS·게임패드를 "동작"과 "축" 단위로 노출하므로,
/// 실기 콕핏 패널을 연결할 때도 호출부는 그대로 두고 여기 바인딩만 바꾸면 된다.
/// 키 바인딩·감도·반전·데드존은 PlayerPrefs 에 저장된다 (설정 화면: InGameMenuUI ▸ CONTROLS).
/// </summary>
public static class SimInput
{
    // ── 고정 바인딩 (재지정 대상 아님) ─────────────────────────────────────────
    public static KeyCode EjectKey      = KeyCode.E;          // 수정키와 함께 (DCS: LCtrl+E ×3)
    public static KeyCode ScoreboardKey = KeyCode.BackQuote;  // 홀드
    public static KeyCode ConfirmKey    = KeyCode.F;          // 상호작용·재출격

    // ── 재지정 가능한 바인딩 ─────────────────────────────────────────────────
    static readonly KeyCode[] Defaults =
    {
        KeyCode.W, KeyCode.S, KeyCode.LeftShift,
        KeyCode.A, KeyCode.D,
        KeyCode.Q, KeyCode.E, KeyCode.B,
        KeyCode.Space, KeyCode.Tab, KeyCode.X, KeyCode.Z,
        KeyCode.C, KeyCode.T, KeyCode.V,
        KeyCode.R,
        KeyCode.H, KeyCode.J, KeyCode.G,
        KeyCode.K,
    };

    static readonly string[] Labels =
    {
        "THROTTLE (MIL)", "SPEED BRAKE", "AFTERBURNER",
        "ROLL LEFT", "ROLL RIGHT",
        "BARREL ROLL L + FLARE", "BARREL ROLL R + FLARE", "ZOOM / IMMELMANN",
        "FIRE MISSILE", "LOCK TARGET", "FLARE", "CHAFF",
        "VIEW (COCKPIT/CHASE)", "TARGET CAM (HOLD)", "MISSILE CAM",
        "WEAPON SELECT (AIM-9X/AIM-120)",
        "AIRSHOW SMOKE ON/OFF", "AIRSHOW SMOKE COLORS", "AIRSHOW FORMATION",
        "CALL TANKER",
    };

    public static int ActionCount => Defaults.Length;
    public static string Label(SimAction a) => Labels[(int)a];

    static KeyCode[] _keys;
    static KeyCode[] Keys
    {
        get
        {
            if (_keys == null) Load();
            return _keys;
        }
    }

    public static KeyCode KeyFor(SimAction a) => Keys[(int)a];

    public static void Rebind(SimAction a, KeyCode key)
    {
        // 같은 키를 쓰던 다른 동작은 서로 맞바꾼다 (한 키 두 동작 방지)
        var keys = Keys;
        for (int i = 0; i < keys.Length; i++)
            if (i != (int)a && keys[i] == key) keys[i] = keys[(int)a];
        keys[(int)a] = key;
        Save();
    }

    // ── 설정 값 ──────────────────────────────────────────────────────────────
    public static float MouseSensitivity = 1f;   // 0.3 ~ 3
    public static bool  InvertPitch;
    public static float StickDeadzone    = 0.08f;

    const string PrefKeyPrefix = "sim.key.";
    const string PrefSens      = "sim.mouseSens";
    const string PrefInvert    = "sim.invertPitch";
    const string PrefDeadzone  = "sim.deadzone";

    public static void Load()
    {
        _keys = (KeyCode[])Defaults.Clone();
        for (int i = 0; i < _keys.Length; i++)
            _keys[i] = (KeyCode)PlayerPrefs.GetInt(PrefKeyPrefix + i, (int)Defaults[i]);
        MouseSensitivity = PlayerPrefs.GetFloat(PrefSens, 1f);
        InvertPitch      = PlayerPrefs.GetInt(PrefInvert, 0) != 0;
        StickDeadzone    = PlayerPrefs.GetFloat(PrefDeadzone, 0.08f);
    }

    public static void Save()
    {
        var keys = Keys;
        for (int i = 0; i < keys.Length; i++) PlayerPrefs.SetInt(PrefKeyPrefix + i, (int)keys[i]);
        PlayerPrefs.SetFloat(PrefSens, MouseSensitivity);
        PlayerPrefs.SetInt(PrefInvert, InvertPitch ? 1 : 0);
        PlayerPrefs.SetFloat(PrefDeadzone, StickDeadzone);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        _keys = (KeyCode[])Defaults.Clone();
        MouseSensitivity = 1f; InvertPitch = false; StickDeadzone = 0.08f;
        Save();
    }

    /// <summary>메뉴가 열려 있는 동안 비행 입력을 막는다 (스로틀은 마지막 값을 유지).</summary>
    public static bool Suppressed { get; set; }

    // ── 동작 ─────────────────────────────────────────────────────────────────
    public static bool Held(SimAction a)    => !Suppressed && Input.GetKey(KeyFor(a));
    public static bool Pressed(SimAction a) => !Suppressed && Input.GetKeyDown(KeyFor(a));

    /// <summary>수정키 (Ctrl) — 단일 키 동작과 조합 동작을 구분한다.</summary>
    public static bool Modifier =>
        Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    /// <summary>사출 핸들 당김 1회. 안전장치(연속 3회)는 기체 쪽(PilotStatus)에서 판정한다.</summary>
    public static bool EjectHandlePulled => Modifier && Input.GetKeyDown(EjectKey);

    public static bool ScoreboardHeld => Input.GetKey(ScoreboardKey);

    public static bool ConfirmPressed => Input.GetKeyDown(ConfirmKey);

    /// <summary>우클릭 홀드 — 자유 시점 (비행 축 입력 정지)</summary>
    public static bool FreeLook => !Suppressed && Input.GetMouseButton(1);

    /// <summary>기총 방아쇠 — 마우스 좌클릭 또는 조이스틱 트리거</summary>
    public static bool GunTrigger
    {
        get
        {
            if (Suppressed) return false;
            if (Input.GetMouseButton(0)) return true;
#if ENABLE_INPUT_SYSTEM
            var js = Joystick.current;
            if (js != null && js.trigger != null && js.trigger.isPressed) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.rightShoulder.isPressed) return true;
#endif
            return false;
        }
    }

    // ── 비행 축 ──────────────────────────────────────────────────────────────
    // 피치·요는 "마우스 단위" 각속도 명령 (기존 Mouse X/Y 축과 같은 스케일) — 스틱 최대 변위는 StickRate 에 대응.
    // 롤은 -1..1 (+ = 우), 스로틀은 0..1 레버 위치.
    const float StickRate = 2.4f;

    /// <summary>피치 명령 (+ = 기수 올림)</summary>
    public static float Pitch
    {
        get
        {
            if (Suppressed || FreeLook) return 0f;
            float m = Input.GetAxis("Mouse Y") * MouseSensitivity;
            float s = StickPitch() * StickRate;
            float v = m + s;
            return InvertPitch ? -v : v;
        }
    }

    /// <summary>요 명령 (+ = 우)</summary>
    public static float Yaw
    {
        get
        {
            if (Suppressed || FreeLook) return 0f;
            return Input.GetAxis("Mouse X") * MouseSensitivity + StickYaw() * StickRate;
        }
    }

    /// <summary>수동 롤 (-1..1, + = 우)</summary>
    public static float Roll
    {
        get
        {
            if (Suppressed) return 0f;
            float r = 0f;
            if (Input.GetKey(KeyFor(SimAction.RollLeft)))  r -= 1f;
            if (Input.GetKey(KeyFor(SimAction.RollRight))) r += 1f;
            float s = StickRoll();
            return Mathf.Abs(s) > Mathf.Abs(r) ? s : r;
        }
    }

    static float _lastThrottle;
    static float _leverLast = -1f;
    static bool  _leverActive;   // 레버를 한 번이라도 움직였으면 레버가 키보드보다 우선

    /// <summary>스로틀 레버 위치 0(아이들)..1(밀리터리). 키보드는 홀드=1, 놓으면 아이들.</summary>
    public static float Throttle
    {
        get
        {
            if (Suppressed) return _lastThrottle;
            float t = Input.GetKey(KeyFor(SimAction.ThrottleUp)) ? 1f : 0f;
            float lever = ThrottleLever();
            if (lever >= 0f)
            {
                if (_leverLast >= 0f && Mathf.Abs(lever - _leverLast) > 0.02f) _leverActive = true;
                _leverLast = lever;
                if (_leverActive && t == 0f) t = Mathf.Clamp01(lever / 0.95f);
            }
            _lastThrottle = t;
            return t;
        }
    }

    /// <summary>애프터버너 — 키 홀드 또는 레버 디텐트(95%) 초과</summary>
    public static bool Afterburner
    {
        get
        {
            if (Suppressed) return _lastThrottle >= 1f && _leverActive && _leverLast > 0.95f;
            if (Input.GetKey(KeyFor(SimAction.Afterburner)) || Input.GetKey(KeyCode.RightShift)) return true;
            return _leverActive && _leverLast > 0.95f;
        }
    }

    public static bool SpeedBrake
    {
        get
        {
            if (Suppressed) return false;
            if (Input.GetKey(KeyFor(SimAction.ThrottleDown))) return true;
#if ENABLE_INPUT_SYSTEM
            var gp = Gamepad.current;
            if (gp != null && gp.leftTrigger.ReadValue() > 0.5f) return true;
#endif
            return false;
        }
    }

    // ── 조이스틱 / 게임패드 ──────────────────────────────────────────────────
    /// <summary>연결된 조종 장치 이름 (없으면 null)</summary>
    public static string ControllerName
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Joystick.current != null) return Joystick.current.displayName;
            if (Gamepad.current  != null) return Gamepad.current.displayName;
#endif
            return null;
        }
    }

    static float Shape(float x, float deadzone)
    {
        float a = Mathf.Abs(x);
        if (a <= deadzone) return 0f;
        a = (a - deadzone) / (1f - deadzone);
        return Mathf.Sign(x) * Mathf.Pow(a, 1.6f);   // 지수 커브 — 중앙 부근 미세 조작
    }

    static float StickPitch()
    {
#if ENABLE_INPUT_SYSTEM
        var js = Joystick.current;
        if (js != null && js.stick != null) return Shape(-js.stick.ReadValue().y, StickDeadzone);  // 당기면(+y 아래) 기수 올림
        var gp = Gamepad.current;
        if (gp != null) return Shape(-gp.leftStick.ReadValue().y, StickDeadzone);
#endif
        return 0f;
    }

    static float StickRoll()
    {
#if ENABLE_INPUT_SYSTEM
        var js = Joystick.current;
        if (js != null && js.stick != null) return Shape(js.stick.ReadValue().x, StickDeadzone);
        var gp = Gamepad.current;
        if (gp != null) return Shape(gp.leftStick.ReadValue().x, StickDeadzone);
#endif
        return 0f;
    }

    static float StickYaw()
    {
#if ENABLE_INPUT_SYSTEM
        var js = Joystick.current;
        if (js != null && js.twist != null) return Shape(js.twist.ReadValue(), StickDeadzone + 0.04f);
        var gp = Gamepad.current;
        if (gp != null) return Shape(gp.rightStick.ReadValue().x, StickDeadzone + 0.04f);
#endif
        return 0f;
    }

    /// <summary>스로틀 레버 0..1 (없으면 -1). HOTAS 의 throttle/slider/z 축, 게임패드는 오른쪽 트리거.</summary>
    static float ThrottleLever()
    {
#if ENABLE_INPUT_SYSTEM
        var js = Joystick.current;
        if (js != null)
        {
            var axis = js.TryGetChildControl<AxisControl>("throttle")
                    ?? js.TryGetChildControl<AxisControl>("slider")
                    ?? js.TryGetChildControl<AxisControl>("z");
            if (axis != null) return Mathf.Clamp01((1f - axis.ReadValue()) * 0.5f);  // 레버 앞(-1) = 최대
        }
        var gp = Gamepad.current;
        if (gp != null) return gp.rightTrigger.ReadValue();
#endif
        return -1f;
    }
}
