using UnityEngine;

public class HitEffectSystem : MonoBehaviour
{
    public static HitEffectSystem Instance { get; private set; }

    // 데미지 값은 GameplayConfigSO (Resources/GameplayConfig.asset)에서 중앙 관리.
    // 폭발 사운드는 FlightAudioSystem 이 거리별(근거리/원거리·음속 지연)로 재생한다.

    [Header("VFX 머티리얼 (미할당 시 Resources 폴백)")]
    [Tooltip("Bullet impact의 가산 머티리얼 (Mat_Additive)")]
    [SerializeField] Material _vfxAdditive;
    [Tooltip("Bullet impact의 알파 머티리얼 (Mat_Alpha — 연기)")]
    [SerializeField] Material _vfxAlpha;

    FlightCamera _cam;

    void Awake() => Instance = this;

    void Start() => _cam = FindAnyObjectByType<FlightCamera>();

    // 연출 전용 — 피해 적용은 CombatDamage 가 소유자 클라이언트에서 처리한다.

    // 미사일 피격 (강한 폭발 이펙트)
    public void TriggerHit(Vector3 worldPos, bool hitLocalPlayer)
    {
        ExplosionEffect.Spawn(worldPos);

        if (_cam != null)
        {
            if (hitLocalPlayer) { _cam.TriggerShake(0.38f, 0.75f); FlightPostFX.Impact(1f); }
            else _cam.AddExplosionShake(worldPos, 1f);   // 가까운 폭발은 충격파가 기체를 흔든다
        }

        FlightAudioSystem.NotifyExplosion(worldPos, hitLocalPlayer);
    }

    // 기총 피격 (작은 스파크 이펙트)
    public void TriggerBulletHit(Vector3 worldPos, bool hitLocalPlayer)
    {
        BulletImpactEffect.Spawn(worldPos, _vfxAdditive, _vfxAlpha);

        if (hitLocalPlayer)
        {
            // 외피를 때리는 금속음은 콕핏 안에서 직접 들린다
            SfxPlayer.Play2D(FlightSounds.HitMetal, 0.8f, Random.Range(0.9f, 1.1f));
            _cam?.TriggerShake(0.10f, 0.18f);
        }
        else
            SfxPlayer.Play(FlightSounds.HitFar, worldPos, 0.6f, 15f, 1500f, true, Random.Range(0.85f, 1.15f));
    }
}
