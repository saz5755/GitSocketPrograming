using UnityEngine;

/// <summary>
/// 미사일 모터 연출 — 로컬 MissileController · 원격 RemoteMissileView 공용.
///   · 노즐 화염 (흰 코어 + 주황 외염) + 멀리서도 보이는 모터 불빛 + 주변을 비추는 점광원
///   · 오래 남는 백색 연기 항적 — 굵어지며 흩어지고 바람에 밀린다 (Ace Combat 식 교전 궤적)
///     가까운 구간은 기존 TrailRenderer 리본이 촘촘히 잇고, 파티클이 수 초간 남는다
///   · 3D 모터 굉음 (도플러) — 연소가 끝나면 꺼진다
/// 연소 여부는 같은 오브젝트의 TrailRenderer.emitting 을 따른다 (발사체 코드는 그대로 트레일만 제어).
/// </summary>
public class MissileExhaustFX : MonoBehaviour
{
    const float SmokeLife = 9f;

    TrailRenderer  _trail;
    ParticleSystem _flame, _smoke;
    FXLib.Flare    _glow;
    Light          _light;
    AudioSource    _motor;
    bool           _burning;
    float          _fade;
    float          _seed;   // 미사일마다 깜빡임 위상 다르게

    public static MissileExhaustFX Attach(GameObject missile, TrailRenderer trail)
    {
        var fx = missile.AddComponent<MissileExhaustFX>();
        fx._trail = trail;
        fx.Build();
        return fx;
    }

    void Build()
    {
        Vector3 nozzle = new Vector3(0f, 0f, -1.6f);
        _seed = Random.Range(0f, 100f);

        // 화염 — 미사일 로컬 공간에서 뒤로 짧게 뿜는다
        _flame = FXLib.MakePS("MotorFlame", transform, FXLib.Glow, 60, worldSpace: false, loop: true);
        _flame.transform.localPosition = nozzle;
        _flame.transform.localRotation = Quaternion.LookRotation(Vector3.back);
        {
            var m = _flame.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(18f, 30f);
            m.startSize     = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            m.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.6f), new Color(1f, 0.55f, 0.25f));
            var em = _flame.emission; em.rateOverTime = 260f;
            var sh = _flame.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 3f; sh.radius = 0.08f;
            FXLib.SizeOverLife(_flame, FXLib.Curve(0f, 1f, 1f, 0.35f));
            FXLib.ColorOverLife(_flame, FXLib.Grad(new[] { Color.white, new Color(1f, 0.45f, 0.1f) }, new[] { 0f, 1f },
                new[] { 1f, 0f }, new[] { 0f, 1f }));
            var r = _flame.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1.5f;
        }

        _glow = FXLib.MakeFlare("MotorGlow", transform, nozzle + Vector3.back * 0.3f, 1.4f, 0.004f, 0.25f);

        _light = new GameObject("MotorLight").AddComponent<Light>();
        _light.transform.SetParent(transform, false);
        _light.transform.localPosition = nozzle + Vector3.back * 1.5f;
        _light.type = LightType.Point;
        _light.color = new Color(1f, 0.65f, 0.35f);
        _light.range = 18f;
        _light.intensity = 0f;

        // 연기 — 미사일과 독립된 월드 오브젝트 (미사일이 파괴돼도 항적이 남는다)
        _smoke = FXLib.MakePS("MissileSmoke", null, FXLib.MissileSmoke, 1500, worldSpace: true, loop: true);
        _smoke.transform.position = transform.TransformPoint(nozzle);
        {
            var m = _smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(SmokeLife * 0.6f, SmokeLife);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            m.startSize     = new ParticleSystem.MinMaxCurve(2f, 2.8f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.gravityModifier = -0.004f;
            var em = _smoke.emission; em.rateOverDistance = 0.8f;   // 입자 지름보다 촘촘히 — 이어진 항적
            var sh = _smoke.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
            var vel = _smoke.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);    // 일정한 바람에 항적 전체가 밀린다
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            var rot = _smoke.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            FXLib.SizeOverLife(_smoke, FXLib.Curve(0f, 0.8f, 0.1f, 1.3f, 1f, 4f));
            FXLib.ColorOverLife(_smoke, FXLib.Grad(
                new[] { new Color(1f, 0.92f, 0.82f), Color.white, new Color(0.9f, 0.9f, 0.92f) }, new[] { 0f, 0.1f, 1f },
                new[] { 0f, 0.95f, 0.5f, 0f }, new[] { 0f, 0.03f, 0.45f, 1f }));
        }

        _motor = gameObject.AddComponent<AudioSource>();
        _motor.clip = FlightSounds.MissileMotor;
        _motor.loop = true;
        _motor.spatialBlend = 1f;
        _motor.rolloffMode = AudioRolloffMode.Logarithmic;
        _motor.minDistance = 20f;
        _motor.maxDistance = 4000f;
        _motor.dopplerLevel = 1.2f;
        _motor.volume = 0f;
        _motor.priority = 60;
        _motor.timeSamples = Random.Range(0, _motor.clip.samples);
        _motor.Play();
    }

    void LateUpdate()
    {
        bool burning = _trail == null || _trail.emitting;
        if (burning != _burning)
        {
            _burning = burning;
            if (burning) { _flame.Play(); _smoke.Play(); }
            else
            {
                _flame.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                _smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
        _fade = Mathf.MoveTowards(_fade, burning ? 1f : 0f, Time.deltaTime * (burning ? 12f : 2.5f));

        if (_smoke != null) _smoke.transform.position = transform.TransformPoint(0f, 0f, -1.8f);
        float flick = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 25f, _seed);
        _glow?.Set(new Color(1f, 0.72f, 0.45f), 9f * _fade * flick);
        if (_light != null) _light.intensity = 4f * _fade * flick;
        if (_motor != null) _motor.volume = 0.9f * _fade;
    }

    void OnDestroy()
    {
        // 항적 연기는 남겨 두고 수명이 다하면 정리
        if (_smoke != null)
        {
            _smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(_smoke.gameObject, SmokeLife + 0.5f);
        }
    }
}
