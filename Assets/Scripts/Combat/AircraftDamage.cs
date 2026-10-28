using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// 부위별 손상 모델 (v4.2 C-5) — 로컬 조종 기체 전용.
/// 총 HP(PlayerController)는 그대로 격추 판정에 쓰고, 같은 피격이 피격 방향에 따라 부위에도 나뉘어 들어가
/// 엔진·날개·유압·연료 계통이 기체 성능을 깎는다.
///   · 엔진   0 → ENGINE FIRE, 추력 0 / 50% 이하 → 추력 70%
///   · 좌/우익 0 → 롤 권한 50% + 손상 쪽으로 롤 쏠림
///   · 유압  ≤10 → HYD LOW, 조종면 반응 지연 / 0 → 지연 2배 + 권한 70%
///   · 연료 누설: 기총 다중 피격 시 확률 → 연료가 계속 샌다
/// 피해 권한 규칙은 CombatDamage 와 같다 — 피격 기체 소유자(본인)만 적용한다.
/// </summary>
public class AircraftDamage : MonoBehaviour
{
    public enum Part { Engine, WingL, WingR, Hydraulics }

    static readonly float[] MaxHp = { 40f, 25f, 25f, 30f };
    readonly float[] _hp = (float[])MaxHp.Clone();

    public bool  FuelLeak { get; private set; }
    int _gunHits;

    /// <summary>피격 (부위, 피격 방향 — 기체 로컬 공간, 0..1 남은 비율)</summary>
    public static event Action<Vector3> OnHitDirection;
    public event Action<Part> OnPartFailed;

    public float Health(Part p)  => _hp[(int)p] / MaxHp[(int)p];
    public bool  Failed(Part p)  => _hp[(int)p] <= 0f;
    public bool  EngineFire      => Failed(Part.Engine);
    public bool  HydLow          => _hp[(int)Part.Hydraulics] <= 10f;

    // ── 비행 모델에 주는 영향 ───────────────────────────────────────────────
    public float ThrustMult => EngineFire ? 0f : Health(Part.Engine) <= 0.5f ? 0.7f : 1f;

    public float RollAuthority
    {
        get
        {
            float a = 1f;
            if (Failed(Part.WingL) || Failed(Part.WingR)) a *= 0.5f;
            if (Failed(Part.Hydraulics)) a *= 0.7f;
            return a;
        }
    }

    public float PitchYawAuthority => Failed(Part.Hydraulics) ? 0.7f : 1f;

    /// <summary>날개 비대칭 양력 — 손상 쪽으로 기우는 롤 각속도 (deg/s, + = 좌)</summary>
    public float RollBias
    {
        get
        {
            float l = 1f - Health(Part.WingL), r = 1f - Health(Part.WingR);
            return (l - r) * 14f;
        }
    }

    /// <summary>조종면 응답 시정수 (s) — 0 이면 즉시</summary>
    public float ControlLag => Failed(Part.Hydraulics) ? 0.35f : HydLow ? 0.18f : 0f;

    public float FuelLeakRate => FuelLeak ? 25f : 0f;

    public bool AnyDamage
    {
        get
        {
            for (int i = 0; i < _hp.Length; i++) if (_hp[i] < MaxHp[i]) return true;
            return FuelLeak;
        }
    }

    public void Repair()
    {
        for (int i = 0; i < _hp.Length; i++) _hp[i] = MaxHp[i];
        FuelLeak = false;
        _gunHits = 0;
    }

    /// <summary>
    /// 피격 분배. fromWorld: 공격자 위치(모르면 null → 무작위 방향).
    /// 미사일은 근접 폭발이라 여러 부위에 나뉘고, 기총은 한 부위에 집중된다.
    /// </summary>
    public void ApplyHit(float amount, string weapon, Vector3? fromWorld)
    {
        Vector3 local = fromWorld.HasValue
            ? transform.InverseTransformDirection((fromWorld.Value - transform.position).normalized)
            : Random.onUnitSphere;
        OnHitDirection?.Invoke(local);

        // 방향 가중치: 뒤 → 엔진, 좌/우 → 해당 날개, 아래 → 유압
        float[] w =
        {
            0.6f + Mathf.Max(0f, -local.z) * 2.2f,
            0.4f + Mathf.Max(0f, -local.x) * 2.4f,
            0.4f + Mathf.Max(0f,  local.x) * 2.4f,
            0.5f + Mathf.Max(0f, -local.y) * 1.8f,
        };

        bool missile = weapon == CombatDamage.WeaponMissile;
        if (missile)
        {
            // 파편이 여러 부위에 — 가중치 비율로 분배 (총량의 1.2배: 부위 피해는 총 HP 와 별개 풀)
            float sum = 0f; foreach (var x in w) sum += x;
            for (int i = 0; i < w.Length; i++) Damage((Part)i, amount * 1.2f * w[i] / sum);
        }
        else
        {
            Damage(Pick(w), amount * 1.4f);
            _gunHits++;
            if (!FuelLeak && _gunHits >= 3 && Random.value < 0.25f) FuelLeak = true;
        }
    }

    void Damage(Part p, float amount)
    {
        int i = (int)p;
        if (_hp[i] <= 0f) return;
        _hp[i] = Mathf.Max(0f, _hp[i] - amount);
        if (_hp[i] <= 0f) OnPartFailed?.Invoke(p);
    }

    static Part Pick(float[] w)
    {
        float sum = 0f; foreach (var x in w) sum += x;
        float r = Random.value * sum;
        for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f) return (Part)i; }
        return Part.Engine;
    }
}
