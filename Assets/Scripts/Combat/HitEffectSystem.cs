using UnityEngine;

public class HitEffectSystem : MonoBehaviour
{
    public static HitEffectSystem Instance { get; private set; }

    [SerializeField] AudioClip explosionClip;

    FlightCamera _cam;
    AudioSource  _audio;

    void Awake()
    {
        Instance = this;
        _audio   = gameObject.AddComponent<AudioSource>();
    }

    void Start() => _cam = FindObjectOfType<FlightCamera>();

    public void TriggerHit(Vector3 worldPos, bool hitLocalPlayer)
    {
        ExplosionEffect.Spawn(worldPos);

        if (_audio != null && explosionClip != null)
            _audio.PlayOneShot(explosionClip, hitLocalPlayer ? 1.0f : 0.6f);

        if (hitLocalPlayer) _cam?.TriggerShake(0.38f, 0.75f);
    }
}
