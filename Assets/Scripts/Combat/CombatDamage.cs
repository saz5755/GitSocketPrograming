/// <summary>
/// 전투 피해 권한 규칙 (멀티플레이 일관성의 기준).
///  · 명중 판정: 무기를 시뮬레이션한 클라이언트 — 발사자 본인, AI 가 쏜 무기는 호스트
///  · 피해 적용: 피격체를 소유한 클라이언트 — 플레이어 기체는 본인, AI 기체는 호스트
/// 판정 측은 MISSILE_DESTROY / GUN_HIT 에 피격자를 실어 보내고, 수신한 소유자가 Apply 한다.
/// </summary>
public static class CombatDamage
{
    public const string WeaponMissile = "MSL";
    public const string WeaponGun     = "GUN";

    static string MyNick => NetworkManager.Instance?.socketClient?.myNickname ?? "";
    static bool   IsHost => AIManager.Instance != null && AIManager.Instance.IsHost;

    /// <summary>이 클라이언트가 shooter 의 무기를 시뮬레이션(명중 판정)하는가.</summary>
    public static bool SimulatesWeaponOf(string shooter)
        => !string.IsNullOrEmpty(shooter) && (shooter == MyNick || (IsHost && AIManager.IsAIBot(shooter)));

    /// <summary>이 클라이언트가 victim 기체의 피해를 적용할 권한이 있는가.</summary>
    public static bool Owns(string victim)
        => !string.IsNullOrEmpty(victim) && (victim == MyNick || (IsHost && AIManager.IsAIBot(victim)));

    /// <summary>소유자일 때만 피해 적용 (아니면 무시).</summary>
    public static void Apply(string victim, float amount, string attacker, string weapon)
    {
        if (!Owns(victim) || amount <= 0f) return;
        if (victim == MyNick) PilotStatus.Instance?.TakeHit(amount, attacker, weapon);
        else                  AIManager.Instance?.DamageBot(victim, amount, attacker, weapon);
    }

    /// <summary>명중 확인 — 내가 쏜 무기면 히트 마커 표시.</summary>
    public static void ReportHit(string shooter, string victim, string weapon)
    {
        if (string.IsNullOrEmpty(victim) || shooter != MyNick || victim == MyNick) return;
        CombatFeedUI.Instance?.ShowHitMarker(weapon == WeaponMissile);
    }
}
