using UnityEngine;

// F-35 스타일 공력 물리 계산기 (순수 정적 — MonoBehaviour 없음)
// 양력·항력·추력·중력을 물리 기반으로 속도 벡터에 적분한다.
//
// 핵심 파라미터 근거 (게임 스케일 기준):
//   · 실속 속도  ≈ 25 m/s  (최대 양력으로도 중력을 못 이길 속도)
//   · 순항 속도  ≈ 50 m/s  (양력 = 중력, AoA ≈ 2°)
//   · 최고 속도  ≈ 80 m/s  (추력 = 항력, W키 유지 시 수렴)
public static class FlightPhysics
{
    // ── 대기 ─────────────────────────────────────────────────────────────────
    const float RHO = 1.225f;           // 해수면 공기 밀도 (kg/m³)

    // ── 기체 파라미터 ─────────────────────────────────────────────────────────
    const float LIFT_K       = 0.0204f; // 유효 S/m  (날개면적÷가상질량)
    const float DRAG_K       = 0.0204f; // 항력용 동일 계수
    const float CD0          = 0.10f;   // 유해항력 계수 (게임 튜닝값, 실측 ~0.018)
    const float CL_SLOPE     = 4.5f;    // 양력 곡선 기울기 (rad⁻¹, 이론치 2π≈6.28)
    const float CL_MAX       = 1.2f;    // 최대 양력 계수 (실속 직전 피크)
    const float STALL_AOA    = 18f;     // 실속 받음각 (도)
    const float ASPECT_RATIO = 2.61f;   // F-35A 날개 종횡비
    const float OSWALD       = 0.78f;   // Oswald 효율 계수
    const float MAX_THRUST   = 8.0f;    // 최대 추력 가속도 (m/s²)  W키
    const float BRAKE_ACC    = 12.0f;   // 감속 추가 항력 (m/s²)    S키
    const float CAMBER_AOA   = 2.0f;    // 날개 캠버 영양력 받음각 보정 (도)
    const float MAX_SPEED    = 150f;    // 물리 발산 방지용 속도 상한

    /// <summary>
    /// 한 타임스텝 공력을 적분하여 새 속도 벡터를 반환한다.
    /// </summary>
    /// <param name="velocity">현재 속도 벡터 (월드 공간, m/s)</param>
    /// <param name="rotation">항공기 자세</param>
    /// <param name="throttle">스로틀 입력: 1=풀 추력  0=아이들  -1=감속</param>
    /// <param name="dt">Time.deltaTime</param>
    public static Vector3 Step(Vector3 velocity, Quaternion rotation, float throttle, float dt)
    {
        float   speed = velocity.magnitude;
        Vector3 fwd   = rotation * Vector3.forward;
        Vector3 up    = rotation * Vector3.up;

        // ── 받음각 (Angle of Attack) ──────────────────────────────────────────
        // 속도 벡터와 기수(forward) 사이 각도 + 캠버 보정
        // → 수평 순항 중에도 최소 CAMBER_AOA만큼 양력이 발생하도록 함
        float aoa = (speed > 0.5f
            ? Vector3.Angle(velocity.normalized, fwd)
            : 0f) + CAMBER_AOA;

        float aoaRad = Mathf.Min(aoa, STALL_AOA + 15f) * Mathf.Deg2Rad;

        // ── 실속 계수: STALL_AOA 초과 시 양력 급감 ───────────────────────────
        float stallFactor = aoa <= STALL_AOA
            ? 1f
            : Mathf.Clamp01(1f - (aoa - STALL_AOA) / 12f);

        // ── 양력 계수 Cl (선형 모델 + 실속 감쇠) ─────────────────────────────
        float Cl = Mathf.Min(CL_SLOPE * Mathf.Sin(2f * aoaRad), CL_MAX) * stallFactor;

        // ── 유도항력 계수 CDi (양력 발생에 따른 부산물) ──────────────────────
        float CDi = (Cl * Cl) / (Mathf.PI * ASPECT_RATIO * OSWALD);

        // ── 동압 q = ½ρv² ────────────────────────────────────────────────────
        float q = 0.5f * RHO * speed * speed;

        // ── 각 힘에 의한 가속도 (m/s²) ───────────────────────────────────────
        float liftAcc  = q * LIFT_K * Cl;
        float dragAcc  = q * DRAG_K * (CD0 + CDi);
        float thrustAcc = throttle > 0f ? MAX_THRUST * throttle : 0f;
        float brakeAcc  = throttle < 0f ? BRAKE_ACC  * (-throttle) : 0f;

        // ── 양력 방향: 속도 벡터에 수직, 기체 위쪽 반구 ─────────────────────
        Vector3 liftDir = Vector3.up;
        if (speed > 0.5f)
        {
            Vector3 vDir  = velocity.normalized;
            Vector3 cross = Vector3.Cross(vDir, up);
            if (cross.sqrMagnitude > 1e-4f)
                liftDir = Vector3.Cross(cross.normalized, vDir).normalized;
        }

        // ── 속도 적분 ─────────────────────────────────────────────────────────
        Vector3 v = velocity;

        v += fwd * thrustAcc * dt;                              // 추력 (기수 방향)
        if (speed > 0.01f)
            v -= velocity.normalized * (dragAcc + brakeAcc) * dt; // 항력 (속도 반대)
        v += liftDir * liftAcc * dt;                            // 양력 (위쪽)
        v += Physics.gravity * dt;                              // 중력 (아래쪽)

        return Vector3.ClampMagnitude(v, MAX_SPEED);            // 발산 방지
    }

    /// <summary>
    /// 현재 받음각 (도, 부호 있음) — HUD 표시용
    /// 양수 = 기수 위 / 음수 = 기수 아래
    /// </summary>
    public static float GetAoA(Vector3 velocity, Quaternion rotation)
    {
        float speed = velocity.magnitude;
        if (speed < 0.5f) return 0f;
        Vector3 right = rotation * Vector3.right;
        return Vector3.SignedAngle(velocity.normalized, rotation * Vector3.forward, right);
    }

    /// <summary>
    /// 현재 G-력 근사 (가속도 벡터 크기 / 9.81)
    /// </summary>
    public static float GetGForce(Vector3 velocity, Vector3 prevVelocity, float dt)
    {
        if (dt < 1e-5f) return 1f;
        Vector3 accel = (velocity - prevVelocity) / dt;
        // 중력 제거 후 체감 G = (총가속도 - 중력) / g
        float bodyAccel = (accel - Physics.gravity).magnitude;
        return bodyAccel / 9.81f;
    }
}
