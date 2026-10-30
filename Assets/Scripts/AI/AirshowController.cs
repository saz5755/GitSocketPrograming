using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 에어쇼 편대장 지휘 (v4.4, AIRSHOW 방 전용) — 실제 곡예비행팀(블랙이글스·블루엔젤스) 운용을 따른다.
///   [H] 스모크 ON/OFF   — 편대장이 "스모크 온" 하면 대형 안의 전 기체가 같은 순간 켠다
///   [J] 스모크 색 구성   — 백색 / 트리컬러(좌 적·우 청) / 컬러(기체별)
///   [G] 대형 전환       — ESCORT · VIC · DIAMOND · ECHELON · LINE ABREAST · TRAIL
///       전환 중 좌우를 가로지르는 기체는 한 층 아래로 지나간다 (EscortAI 크로스언더)
///
/// 동기화: 스모크는 PlayerController.SmokeFx → MOVE(로컬)·AI_MOVE_BATCH(편대) 의 fx 바이트로 실려 가고,
/// 대형은 위치 자체로 전달된다 — 추가 패킷 없음.
/// 편대(에스코트 AI)는 방장 클라이언트에만 있으므로 대형 지휘는 방장(편대장)만 가능. 스모크는 누구나.
/// AIManager 가 같은 오브젝트에 붙인다.
/// </summary>
public class AirshowController : MonoBehaviour
{
    public enum Preset { Escort, Vic, Diamond, Echelon, LineAbreast, Trail }
    public enum Scheme { White, Tricolor, Colors }

    static readonly string[] PresetNames = { "ESCORT", "VIC", "DIAMOND", "ECHELON RIGHT", "LINE ABREAST", "TRAIL" };
    static readonly string[] SchemeNames = { "WHITE", "TRICOLOR", "COLORS" };
    static readonly int[]    ColorOrder  = { 1, 2, 3, 4, 5, 6, 7 };

    // 밀집 대형 간격 (m) — KF-21 날개폭 약 11m, 날개 끝 간 2~3m 여유
    const float Lat = 13f, Back = 11f, Step = 1.5f;

    public static AirshowController Instance { get; private set; }
    public bool   SmokeOn  => _smoke;
    public Preset Current  => _preset;
    public Scheme Colors   => _scheme;

    bool   _smoke;
    Scheme _scheme;
    Preset _preset;
    readonly List<EscortAI> _roles = new();

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        var gm = GameModeManager.Instance;
        PlayerController local = null;
        if (gm != null && gm.LocalAircraft != null) local = gm.LocalAircraft.GetComponent<PlayerController>();
        bool flying = local != null && gm.IsFlying && !gm.IsDead;
        bool airshow = RoomModeSO.IsAirshow;

        if (!airshow || !flying)
        {
            // 착함·하차·사망 — 스모크는 끄고(다음 출격에 새로 켠다) 대형은 유지
            if (_smoke) _smoke = false;
            if (local != null) local.SmokeFx = 0;
            ApplyEscorts(false);
            return;
        }

        if (SimInput.Pressed(SimAction.SmokeToggle)) ToggleSmoke();
        if (SimInput.Pressed(SimAction.SmokeColor))  CycleColors();
        if (SimInput.Pressed(SimAction.Formation))   CycleFormation();

        local.SmokeFx = AirshowSmokeFX.Encode(_smoke, 0);   // 편대장은 항상 백색
        ApplyEscorts(_smoke);
    }

    // ── 지휘 (키 입력·UI·테스트 공용) ─────────────────────────────────────
    public void ToggleSmoke()
    {
        _smoke = !_smoke;
        Notice(_smoke ? $"SMOKE ON  ·  {SchemeNames[(int)_scheme]}" : "SMOKE OFF");
    }

    public void CycleColors()
    {
        _scheme = (Scheme)(((int)_scheme + 1) % SchemeNames.Length);
        Notice($"SMOKE COLORS  ·  {SchemeNames[(int)_scheme]}");
    }

    public void CycleFormation() => SetFormation((Preset)(((int)_preset + 1) % PresetNames.Length));

    public void SetFormation(Preset p)
    {
        CollectRoles();
        if (_roles.Count == 0) { Notice("편대 지휘는 편대장(방장)만 할 수 있습니다", warn: true); return; }
        _preset = p;
        ApplyFormation();
        Notice($"FORMATION  ·  {PresetNames[(int)_preset]}");
    }

    // 편대 역할 순서: 기존 슬롯 기준 안쪽부터 좌·우 교대 (L1, R1, L2, R2 …)
    void CollectRoles()
    {
        _roles.Clear();
        var left = new List<EscortAI>();
        var right = new List<EscortAI>();
        foreach (var e in EscortAI.AllEscorts)
        {
            if (e == null) continue;
            (e.BaseOffset.x < 0f ? left : right).Add(e);
        }
        left.Sort((a, b) => Mathf.Abs(a.BaseOffset.x).CompareTo(Mathf.Abs(b.BaseOffset.x)));
        right.Sort((a, b) => Mathf.Abs(a.BaseOffset.x).CompareTo(Mathf.Abs(b.BaseOffset.x)));
        for (int i = 0; i < Mathf.Max(left.Count, right.Count); i++)
        {
            if (i < left.Count) _roles.Add(left[i]);
            if (i < right.Count) _roles.Add(right[i]);
        }
    }

    void ApplyFormation()
    {
        for (int k = 0; k < _roles.Count; k++)
        {
            var e = _roles[k];
            bool airshow = _preset != Preset.Escort;
            e.SetFormationTarget(airshow ? Offset(_preset, k, e.BaseOffset.x < 0f) : e.BaseOffset, airshow);
        }
    }

    /// <summary>역할 k(0부터) 의 대형 오프셋 (리더 기준 로컬: x 우, y 위, z 앞)</summary>
    static Vector3 Offset(Preset p, int k, bool leftRole)
    {
        int   r = k / 2 + 1;                     // 좌우 쌍의 순위
        float s = leftRole ? -1f : 1f;
        switch (p)
        {
            case Preset.Vic:
                return new Vector3(s * Lat * r, -Step * r, -Back * r);
            case Preset.Diamond:
                if (k < 2) return new Vector3(s * Lat, -Step, -Back);
                // 슬롯(리더 정후방, 제트 후류를 피해 한 층 아래) — 이후 기체는 그 뒤로 일렬
                return new Vector3(0f, -Step * 2f - 2f * (k - 2), -Back * 2f - 12f * (k - 2));
            case Preset.Echelon:
                return new Vector3(Lat * (k + 1), -Step * (k + 1), -Back * (k + 1));
            case Preset.LineAbreast:
                return new Vector3(s * 16f * r, -0.5f * r, -2f * r);
            case Preset.Trail:
                return new Vector3(0f, -2.5f * (k + 1), -18f * (k + 1));
            default:
                return Vector3.zero;
        }
    }

    void ApplyEscorts(bool smoke)
    {
        if (EscortAI.AllEscorts.Count == 0) return;
        if (_roles.Count != EscortAI.AllEscorts.Count) CollectRoles();
        for (int k = 0; k < _roles.Count; k++)
        {
            var e = _roles[k];
            if (e == null) continue;
            var pc = e.GetComponent<PlayerController>();
            if (pc == null) continue;
            int color = _scheme switch
            {
                Scheme.Tricolor => e.BaseOffset.x < 0f ? 1 : 2,
                Scheme.Colors   => ColorOrder[k % ColorOrder.Length],
                _               => 0,
            };
            // 대형 안에 있는 기체만 — 발진·착함·마셜 중인 기체는 연막을 쓰지 않는다
            pc.SmokeFx = AirshowSmokeFX.Encode(smoke && e.IsInFormation, color);
        }
    }

    static void Notice(string msg, bool warn = false) => CombatFeedUI.Instance?.ShowNotice(msg, warn);
}
