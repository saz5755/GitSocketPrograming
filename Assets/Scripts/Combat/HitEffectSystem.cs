using UnityEngine;

public class HitEffectSystem : MonoBehaviour
{
    public static HitEffectSystem Instance { get; private set; }

    [SerializeField] AudioClip explosionClip;

    [Header("VFX 프리팹/머티리얼 (미할당 시 Resources 폴백)")]
    [Tooltip("미사일 피격 폭발 프리팹")]
    [SerializeField] GameObject _explosionPrefab;
    [Tooltip("Bullet impact의 가산 머티리얼 (Mat_Additive)")]
    [SerializeField] Material _vfxAdditive;
    [Tooltip("Bullet impact의 알파 머티리얼 (Mat_Alpha — 연기)")]
    [SerializeField] Material _vfxAlpha;

    FlightCamera _cam;
    AudioSource  _audio;

    void Awake()
    {
        Instance = this;
        _audio   = gameObject.AddComponent<AudioSource>();
    }

    void Start() => _cam = FindFirstObjectByType<FlightCamera>();

    // 미사일 피격 (강한 폭발 이펙트)
    public void TriggerHit(Vector3 worldPos, bool hitLocalPlayer)
    {
        ExplosionEffect.Spawn(worldPos, _explosionPrefab);

        if (_audio != null && explosionClip != null)
            _audio.PlayOneShot(explosionClip, hitLocalPlayer ? 1.0f : 0.6f);

        if (hitLocalPlayer) _cam?.TriggerShake(0.38f, 0.75f);

        FlightAudioSystem.NotifyExplosion(worldPos, hitLocalPlayer);
    }

    // 기총 피격 (작은 스파크 이펙트)
    public void TriggerBulletHit(Vector3 worldPos, bool hitLocalPlayer)
    {
        BulletImpactEffect.Spawn(worldPos, _vfxAdditive, _vfxAlpha);

        if (_audio != null)
            _audio.PlayOneShot(MakeBulletImpactClip(), hitLocalPlayer ? 0.55f : 0.3f);

        if (hitLocalPlayer) _cam?.TriggerShake(0.10f, 0.18f);
    }

    static AudioClip MakeBulletImpactClip()
    {
        int sr = 22050, n = (int)(sr * 0.12f);
        var buf = new float[n];
        var rng = new System.Random();
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / n;
            float env = Mathf.Exp(-t * 30f);
            buf[i] = (float)(rng.NextDouble() * 2 - 1) * env * 0.9f;
        }
        var clip = AudioClip.Create("BulletImpact", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }
}
