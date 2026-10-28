using System.Numerics;

/// <summary>
/// 위치 이력 — 최근 약 3초(20Hz × 64)의 월드 위치. 피격 검증 시 "그 순간 거기 있었는가"를 되감아 본다.
/// </summary>
public class PoseHistory
{
    const int Capacity = 64;
    readonly long[]    _t = new long[Capacity];
    readonly Vector3[] _p = new Vector3[Capacity];
    int _head, _count;

    public void Add(float x, float y, float z)
    {
        lock (this)
        {
            _t[_head] = DateTime.UtcNow.Ticks;
            _p[_head] = new Vector3(x, y, z);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    public bool HasData { get { lock (this) return _count > 0; } }

    public Vector3 Latest
    {
        get { lock (this) return _count == 0 ? Vector3.Zero : _p[(_head - 1 + Capacity) % Capacity]; }
    }

    /// <summary>최근 window 안의 위치(구간 선형 보간 포함) 중 point 와 가장 가까운 거리</summary>
    public float MinDistance(Vector3 point, TimeSpan window)
    {
        lock (this)
        {
            if (_count == 0) return float.MaxValue;
            long since = DateTime.UtcNow.Ticks - window.Ticks;
            float best = float.MaxValue;
            Vector3? prev = null;
            for (int k = 0; k < _count; k++)
            {
                int i = (_head - 1 - k + Capacity * 2) % Capacity;
                var cur = _p[i];
                best = Math.Min(best, prev.HasValue ? SegmentDistance(point, cur, prev.Value) : Vector3.Distance(point, cur));
                prev = cur;
                if (_t[i] < since) break;   // 창 바로 바깥 한 점까지 포함 (구간 보간)
            }
            return best;
        }
    }

    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 > 1e-6f ? Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }
}

/// <summary>
/// 서버 권위 피격 검증 (v4.3 N-4).
/// 명중 판정 자체는 여전히 무기를 시뮬레이션한 클라이언트가 하지만(CombatDamage 규칙),
/// 서버가 위치 이력으로 되감아(지연 보상) 그럴듯한지 확인하고, 아니면 버리거나 빗나감으로 바꿔 중계한다.
///   · 발사자 권한: 본인 이름으로만 쏠 수 있고, BOT_* 무기는 호스트만 보고 가능
///   · 미사일: 서버가 본 MISSILE_SPAWN 이 있어야 하고(1회용 — 중복 명중 차단), 수명·피해 상한 확인
///   · 위치: 보고된 명중 지점이 피격자의 최근 RewindWindow 경로 근처여야 한다
///   · 기총: 사거리, 초당 명중 수 상한
/// </summary>
public static class HitValidation
{
    public const float MaxMissileDamage = 60f;
    const float GunMaxRange          = 2500f;
    const float GunHitTolerance      = 35f;    // 기체 크기 + 20Hz 위치 샘플 사이 이동
    const float MissileHitTolerance  = 60f;    // 근접신관 반경 포함
    const int   MaxGunHitsPerSecond  = 40;
    static readonly TimeSpan RewindWindow   = TimeSpan.FromMilliseconds(700);   // 왕복 지연 + 보간 지연 여유
    static readonly TimeSpan MissileMaxLife = TimeSpan.FromSeconds(45);

    public static bool CanActAs(Room room, Player sender, string? shooter)
        => !string.IsNullOrEmpty(shooter) &&
           (shooter == sender.nickname || (shooter.StartsWith("BOT_") && sender.nickname == room.hostNickname));

    public static bool ValidateGunHit(Room room, Player sender, GunHitPacket p, out string reason)
    {
        reason = "";
        if (!CanActAs(room, sender, p.shooterNickname)) { reason = "shooter mismatch"; return false; }
        if (string.IsNullOrEmpty(p.targetNickname) || p.targetNickname == p.shooterNickname) { reason = "bad target"; return false; }
        if (!room.CountGunHit(p.shooterNickname!, MaxGunHitsPerSecond)) { reason = "rate"; return false; }

        var hit = new Vector3(p.posX, p.posY, p.posZ);
        var victim = room.History(p.targetNickname);
        if (victim != null && victim.HasData && victim.MinDistance(hit, RewindWindow) > GunHitTolerance)
        { reason = "victim not at hit point"; return false; }

        var shooter = room.History(p.shooterNickname!);
        if (shooter != null && shooter.HasData && shooter.MinDistance(hit, RewindWindow) > GunMaxRange)
        { reason = "out of gun range"; return false; }
        return true;
    }

    /// <summary>false 면 피격자를 지우고(빗나감) 중계한다 — 미사일 소멸 연출은 그대로 보여야 하므로 버리지 않는다</summary>
    public static bool ValidateMissileHit(Room room, Player sender, MissileDestroyPacket p, out string reason)
    {
        reason = "";
        if (!room.TakeMissile(p.missileId, out var shooter, out var spawnedAt)) { reason = "unknown missile"; return false; }
        if (shooter != p.shooterNickname) { reason = "shooter mismatch"; return false; }
        if (!CanActAs(room, sender, p.shooterNickname)) { reason = "not authority"; return false; }
        if (string.IsNullOrEmpty(p.hitNickname)) return true;   // 빗나감 보고 — 검증할 것 없음
        if (DateTime.UtcNow - spawnedAt > MissileMaxLife) { reason = "expired"; return false; }

        var hit = new Vector3(p.posX, p.posY, p.posZ);
        var victim = room.History(p.hitNickname);
        if (victim != null && victim.HasData && victim.MinDistance(hit, RewindWindow) > MissileHitTolerance)
        { reason = "victim not at hit point"; return false; }

        p.damage = Math.Clamp(p.damage, 0f, MaxMissileDamage);
        return true;
    }
}
