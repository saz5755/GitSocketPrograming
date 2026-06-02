using UnityEngine;

/// <summary>
/// 콕핏 탑승 중 클릭하는 인터랙션 스위치.
///
/// ■ 배치 (항공기 프리팹 기준)
///   [PlayerController Root]
///   └─ (cockpit mesh 부모)
///      ├─ Switch_APU  [CockpitSwitch, switchIndex=0]
///      ├─ Switch_ENG  [CockpitSwitch, switchIndex=1]
///      └─ Switch_AV   [CockpitSwitch, switchIndex=2]
///
/// ■ 레지스트리 등록 타이밍
///   Awake에서 자동 등록하지 않는다.
///   FlightCamera.BeginCockpitBoarding() 시 로컬 플레이어 항공기에서만 Register() 호출.
///   → 원격 플레이어 항공기의 스위치가 레지스트리를 덮어쓰는 문제 방지.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class CockpitSwitch : MonoBehaviour
{
    public enum State { Locked, Available, Done }

    [SerializeField] int _switchIndex;   // Inspector에서 0 / 1 / 2 설정
    public int SwitchIndex => _switchIndex;

    // ── 상태 색상 ──────────────────────────────────────────────────────────
    static readonly Color ColLocked    = new Color(0.18f, 0.18f, 0.22f);
    static readonly Color ColAvailable = new Color(1.00f, 0.82f, 0.00f);
    static readonly Color ColDone      = new Color(0.00f, 0.90f, 0.35f);

    // ── 정적 레지스트리 — Register/Unregister 로만 갱신 ──────────────────
    static readonly CockpitSwitch[] _registry = new CockpitSwitch[3];

    Renderer _rend;
    Material _mat;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _rend = GetComponent<Renderer>();
        _mat  = new Material(_rend.sharedMaterial != null
            ? _rend.sharedMaterial
            : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")));
        _rend.material = _mat;
        _mat.EnableKeyword("_EMISSION");
        _rend.enabled = false;   // 탑승 모드 진입 전까지 숨김
        Apply(State.Locked);
    }

    void OnDestroy() => Unregister();

    // ── 공개 API ────────────────────────────────────────────────────────────

    // 로컬 플레이어 항공기 탑승 시 FlightCamera에서 호출
    public void Register()
    {
        if (_switchIndex >= 0 && _switchIndex < _registry.Length)
            _registry[_switchIndex] = this;
    }

    // 탑승 종료 시 FlightCamera에서 호출
    public void Unregister()
    {
        if (_switchIndex >= 0 && _switchIndex < _registry.Length
            && _registry[_switchIndex] == this)
            _registry[_switchIndex] = null;
    }

    public static CockpitSwitch Get(int idx) =>
        (idx >= 0 && idx < _registry.Length) ? _registry[idx] : null;

    public void SetState(State state) => Apply(state);
    public void SetVisible(bool visible) => _rend.enabled = visible;

    // ─────────────────────────────────────────────────────────────────────────
    void Apply(State state)
    {
        if (_mat == null) return;
        Color col, em;
        switch (state)
        {
            case State.Available: col = ColAvailable; em = ColAvailable * 0.55f; break;
            case State.Done:      col = ColDone;      em = ColDone      * 0.40f; break;
            default:              col = ColLocked;    em = Color.black;           break;
        }
        _mat.color = col;
        _mat.SetColor("_EmissionColor", em);
    }
}
