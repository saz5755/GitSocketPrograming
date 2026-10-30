using UnityEngine;

/// <summary>
/// 기총 발사 연출 — 총구 섬광(깜빡이는 점광원 + 화염 꽃) · 포구 연기.
/// 로컬은 GunSystem.Fire, 원격·AI 는 GUN_FIRE 수신 시 Fire() 를 부른다.
/// </summary>
public class AircraftGunFX : MonoBehaviour
{
    // 포구 — GunSystem 탄 생성 위치(기체 전방 5m)와 같은 높이
    static readonly Vector3 Muzzle = new Vector3(0f, 0.15f, 4.6f);

    ParticleSystem _flash, _smoke;
    Light _light;
    float _lightT;

    public static AircraftGunFX Ensure(GameObject aircraft)
    {
        var g = aircraft.GetComponent<AircraftGunFX>();
        return g != null ? g : aircraft.AddComponent<AircraftGunFX>();
    }

    void Awake()
    {
        _flash = FXLib.MakePS("MuzzleFlash", transform, FXLib.Glow, 12, worldSpace: false);
        _flash.transform.localPosition = Muzzle;
        {
            var m = _flash.main;
            m.startLifetime = 0.035f;
            m.startSpeed    = 0f;
            m.startSize     = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.45f), new Color(1f, 0.6f, 0.25f));
        }

        _smoke = FXLib.MakePS("GunSmoke", transform, FXLib.Smoke, 80, worldSpace: true);
        _smoke.transform.localPosition = Muzzle;
        {
            var m = _smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(0f, 1f);
            m.startSize     = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            FXLib.SizeOverLife(_smoke, FXLib.Curve(0f, 0.6f, 1f, 2.6f));
            FXLib.ColorOverLife(_smoke, FXLib.Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f },
                new[] { 0.45f, 0f }, new[] { 0f, 1f }));
        }

        _light = new GameObject("MuzzleLight").AddComponent<Light>();
        _light.transform.SetParent(transform, false);
        _light.transform.localPosition = Muzzle + Vector3.up * 0.3f;
        _light.type = LightType.Point;
        _light.color = new Color(1f, 0.7f, 0.35f);
        _light.range = 9f;
        _light.intensity = 0f;
        _light.enabled = false;
    }

    public void Fire()
    {
        _flash.Emit(1);
        if (Random.value < 0.5f) _smoke.Emit(1);
        _lightT = 0.05f;
        _light.enabled = true;
    }

    void Update()
    {
        if (_lightT <= 0f) return;
        _lightT -= Time.deltaTime;
        _light.intensity = _lightT > 0f ? Random.Range(2.5f, 5f) : 0f;
        if (_lightT <= 0f) _light.enabled = false;
    }
}
