using UnityEngine;

/// <summary>
/// 미사일 성능 제원 — 추진(부스트)·항력(코스팅)·기동 한계·시커 짐벌·대응책 저항.
/// 값은 GameplayConfigSO 에서 Inspector 로 조정한다 (이 게임의 기체 속도 스케일 ≈ 실기의 1/3 기준).
///
/// 물리 모델:
///   · 부스트 단계: boostTime 동안 boostAccel 로 가속
///   · 코스팅: dv/dt = −dragK·v² − g·sin(pitch)  → 멀리 쏠수록, 상승할수록 에너지 손실
///   · 가용 G: maxG × (v / fullGSpeed)² 상한 — 느려진 미사일은 꺾지 못해 기동으로 따돌릴 수 있다
///   · v &lt; minGuideSpeed 이면 유도 불능 (운동에너지 고갈 → 자폭)
/// </summary>
[System.Serializable]
public class MissileSpec
{
    public string displayName = "AIM-120";
    public MissileGuidanceType guidance = MissileGuidanceType.RadarGuided;

    [Header("추진 / 항력")]
    [Tooltip("로켓 모터 연소 시간 (s)")]
    public float boostTime  = 3.5f;
    [Tooltip("연소 중 가속도 (m/s²)")]
    public float boostAccel = 130f;
    [Tooltip("항력 계수 k (dv/dt = −k·v²). 클수록 코스팅 중 빨리 감속")]
    public float dragK      = 0.00018f;
    [Tooltip("이 속도 아래로 떨어지면 유도 불능 (m/s)")]
    public float minGuideSpeed = 150f;
    [Tooltip("최대 비행 시간 (s)")]
    public float lifetime   = 40f;

    [Header("기동")]
    [Tooltip("최대 횡가속 (G)")]
    public float maxG       = 35f;
    [Tooltip("이 속도 이상에서 maxG 전부 사용 가능 (m/s). 아래로는 v² 비례 감소")]
    public float fullGSpeed = 380f;
    [Tooltip("비례항법 상수 N")]
    public float navConst   = 4f;

    [Header("시커")]
    [Tooltip("시커 짐벌 반각 (°) — 표적이 이 각도를 벗어나면 락 상실")]
    public float gimbalLimit = 70f;
    [Tooltip("시커 탐지 거리 (m). IR = 발사 전 포착 거리, ARH = 피트불(능동 전환) 거리")]
    public float seekerRange = 2200f;
    [Tooltip("대응책 저항 (0 = 구형, 1 = 완전 무시). IR 은 IRCCM, 레이더는 ECCM")]
    [Range(0f, 1f)] public float ccmResistance = 0.5f;

    [Header("신관 / 탄두")]
    public float proximityFuse = 9f;
    public float directHitDist = 3f;
    [Tooltip("GameplayConfig.missileDamage 배율")]
    public float damageScale   = 1f;

    [Header("발사 조건")]
    [Tooltip("발사 가능 최대 기수 이탈각 (°)")]
    public float launchOffBoresight = 45f;
    [Tooltip("최소 사거리 (m) — 이보다 가까우면 신관 무장 전 통과")]
    public float minRange = 300f;

    public bool IsIR => guidance == MissileGuidanceType.HeatSeeking;

    // ── 기본 제원 ─────────────────────────────────────────────────────────
    // 기체 최고속도 80~120 m/s 스케일에 맞춘 값. 대략 사거리: AIM-9X ≈ 4 km, AIM-120 ≈ 9 km (정지 표적 기준).

    public static MissileSpec Aim9X() => new MissileSpec
    {
        displayName = "AIM-9X", guidance = MissileGuidanceType.HeatSeeking,
        boostTime = 2.0f, boostAccel = 200f, dragK = 0.000375f, minGuideSpeed = 150f, lifetime = 22f,
        maxG = 45f, fullGSpeed = 320f, navConst = 4.2f,
        gimbalLimit = 90f, seekerRange = 3500f, ccmResistance = 0.55f,
        proximityFuse = 8f, damageScale = 1f, launchOffBoresight = 60f, minRange = 250f,
    };

    public static MissileSpec Aim120() => new MissileSpec
    {
        displayName = "AIM-120", guidance = MissileGuidanceType.RadarGuided,
        boostTime = 3.5f, boostAccel = 130f, dragK = 0.00018f, minGuideSpeed = 150f, lifetime = 45f,
        maxG = 35f, fullGSpeed = 380f, navConst = 4f,
        gimbalLimit = 70f, seekerRange = 2200f, ccmResistance = 0.5f,
        proximityFuse = 10f, damageScale = 1.1f, launchOffBoresight = 45f, minRange = 500f,
    };

    /// <summary>적 단거리 IR 미사일 — 시커가 구형이라 플레어에 약하다</summary>
    public static MissileSpec EnemyIR() => new MissileSpec
    {
        displayName = "R-73", guidance = MissileGuidanceType.HeatSeeking,
        boostTime = 1.8f, boostAccel = 190f, dragK = 0.0004f, minGuideSpeed = 145f, lifetime = 20f,
        maxG = 40f, fullGSpeed = 320f, navConst = 4f,
        gimbalLimit = 60f, seekerRange = 3000f, ccmResistance = 0.3f,
        proximityFuse = 8f, damageScale = 1f, launchOffBoresight = 40f, minRange = 300f,
    };

    /// <summary>적 중거리 레이더 미사일</summary>
    public static MissileSpec EnemyRadar() => new MissileSpec
    {
        displayName = "R-77", guidance = MissileGuidanceType.RadarGuided,
        boostTime = 3.2f, boostAccel = 125f, dragK = 0.0002f, minGuideSpeed = 150f, lifetime = 40f,
        maxG = 32f, fullGSpeed = 380f, navConst = 3.8f,
        gimbalLimit = 60f, seekerRange = 2000f, ccmResistance = 0.35f,
        proximityFuse = 10f, damageScale = 1.1f, launchOffBoresight = 35f, minRange = 600f,
    };

    /// <summary>MISSILE_SPAWN 의 weapon 문자열 → 제원 (원격 뷰 표시·경보용)</summary>
    public static MissileSpec ByName(string name, MissileGuidanceType fallback)
    {
        var cfg = GameplayConfigSO.Instance;
        if (name == cfg.aim9x.displayName)      return cfg.aim9x;
        if (name == cfg.aim120.displayName)     return cfg.aim120;
        if (name == cfg.enemyIR.displayName)    return cfg.enemyIR;
        if (name == cfg.enemyRadar.displayName) return cfg.enemyRadar;
        return fallback == MissileGuidanceType.HeatSeeking ? cfg.aim9x : cfg.aim120;
    }

    // ── 발사 가능 구역 (DLZ) ───────────────────────────────────────────────
    // 직선 비행 1D 적분으로 "표적이 openingSpeed 로 멀어질 때 따라잡을 수 있는 최대 초기 거리" 를 구한다.
    //   Rmax : 표적이 현재 속도·방향을 유지할 때
    //   Rne  : 표적이 즉시 반대로 돌아 최고속으로 도망칠 때 (No-Escape)
    public float MaxRangeAgainst(float launchSpeed, float openingSpeed)
    {
        const float dt = 0.1f;
        float v = Mathf.Max(launchSpeed, 40f), d = 0f, best = 0f;
        for (float t = 0f; t < lifetime; t += dt)
        {
            if (t < boostTime) v += boostAccel * dt;
            v -= dragK * v * v * dt;
            if (t > boostTime && v < minGuideSpeed) break;
            d += v * dt;
            best = Mathf.Max(best, d - openingSpeed * (t + dt));
        }
        // 유도 손실(곡선 경로) 여유 15%
        return best * 0.85f;
    }
}
