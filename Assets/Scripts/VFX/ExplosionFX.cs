using UnityEngine;

/// <summary>
/// 다층 폭발 이펙트 (Ace Combat · DCS 류 공중 폭발 연출 참고).
///   1. 섬광 — 순간 강한 점광원 + 빌보드 (블룸으로 번짐)
///   2. 불덩이 — HDR 가산 화염이 부풀며 식는다
///   3. 화염 연기 — 안쪽이 불빛으로 물든 짙은 연기, 천천히 부풀고 떠오른다
///   4. 파편 스파크 + 연기 꼬리를 끌며 포물선으로 떨어지는 파편 (공중 폭발의 상징적 실루엣)
///   5. 충격파 — 화면 굴절 링이 빠르게 퍼진다
///   6. 해면 근처면 물기둥·물보라
/// scale=1 이 미사일 명중, 1.6 정도가 기체 추락.
/// </summary>
public class ExplosionFX : MonoBehaviour
{
    const float Life = 10f;

    Light _light;
    FXLib.Flare _flash;
    float _t, _scale;

    public static void Spawn(Vector3 pos, float scale = 1f)
    {
        var go = new GameObject("ExplosionFX");
        go.transform.position = pos;
        var fx = go.AddComponent<ExplosionFX>();
        fx._scale = scale;
        fx.Build();
        Destroy(go, Life);
    }

    void Build()
    {
        float s = _scale;
        Transform root = transform;
        bool nearSea = transform.position.y - FXLib.SeaY < 22f * s;

        // ── 1. 섬광 ───────────────────────────────────────────────────────
        _light = gameObject.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = new Color(1f, 0.72f, 0.4f);
        _light.range = 140f * s;
        _light.intensity = 0f;
        _flash = FXLib.MakeFlare("Flash", root, Vector3.zero, 9f * s, 0.02f, 0.4f);

        // ── 2. 불덩이 ─────────────────────────────────────────────────────
        var fire = FXLib.MakePS("Fireball", root, FXLib.Glow, 40);
        {
            var m = fire.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(6f * s, 22f * s);
            m.startSize     = new ParticleSystem.MinMaxCurve(4f * s, 9f * s);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.5f, 0.15f));
            var sh = fire.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.5f * s;
            var lv = fire.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.25f; lv.limit = 2f;
            FXLib.SizeOverLife(fire, FXLib.Curve(0f, 0.45f, 0.25f, 1f, 1f, 1.35f));
            FXLib.ColorOverLife(fire, FXLib.Grad(
                new[] { Color.white, new Color(1f, 0.55f, 0.15f), new Color(0.6f, 0.12f, 0.02f) }, new[] { 0f, 0.3f, 1f },
                new[] { 1f, 0.8f, 0f }, new[] { 0f, 0.4f, 1f }));
            FXLib.Burst(fire, 26);
        }

        // ── 3. 화염 연기 ──────────────────────────────────────────────────
        var smoke = FXLib.MakePS("FireSmoke", root, FXLib.SmokeFire, 40);
        {
            var m = smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(4f * s, 12f * s);
            m.startSize     = new ParticleSystem.MinMaxCurve(6f * s, 11f * s);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.gravityModifier = -0.012f;
            m.startColor    = Color.white;
            var sh = smoke.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2f * s;
            var lv = smoke.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.12f; lv.limit = 1.2f;
            var rot = smoke.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            FXLib.SizeOverLife(smoke, FXLib.Curve(0f, 0.5f, 0.2f, 1.1f, 1f, 2.4f));
            FXLib.ColorOverLife(smoke, FXLib.Grad(
                new[] { Color.white, new Color(0.85f, 0.83f, 0.8f), new Color(0.65f, 0.65f, 0.66f) }, new[] { 0f, 0.4f, 1f },
                new[] { 0f, 1f, 0.7f, 0f }, new[] { 0f, 0.05f, 0.5f, 1f }));
            FXLib.Burst(smoke, 18);
        }

        // ── 4a. 스파크 ────────────────────────────────────────────────────
        var spark = FXLib.MakePS("Sparks", root, FXLib.Spark, 80);
        {
            var m = spark.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(35f * s, 90f * s);
            m.startSize     = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            m.startColor    = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.55f, 0.2f));
            m.gravityModifier = 1f;
            var sh = spark.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1f;
            var lv = spark.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.08f; lv.limit = 15f;
            FXLib.ColorOverLife(spark, FXLib.Grad(
                new[] { Color.white, new Color(1f, 0.4f, 0.1f) }, new[] { 0f, 1f },
                new[] { 1f, 1f, 0f }, new[] { 0f, 0.6f, 1f }));
            var r = spark.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 2f;
            FXLib.Burst(spark, 60);
        }

        // ── 4b. 연기 꼬리를 끄는 파편 ─────────────────────────────────────
        int chunks = Random.Range(3, 5) + (s > 1.2f ? 1 : 0);
        for (int i = 0; i < chunks; i++)
        {
            Vector3 dir = (Random.onUnitSphere + Vector3.up * 0.6f).normalized;
            DebrisTrail.Spawn(transform.position, dir * Random.Range(22f, 45f) * Mathf.Sqrt(s), Random.Range(1.0f, 2.0f));
        }

        // ── 5. 충격파 굴절 ────────────────────────────────────────────────
        var shock = FXLib.MakePS("Shockwave", root, FXLib.Shockwave, 2);
        {
            var m = shock.main;
            m.startLifetime = 0.45f;
            m.startSpeed    = 0f;
            m.startSize     = 6f * s;
            FXLib.SizeOverLife(shock, FXLib.Curve(0f, 0.1f, 1f, 12f));
            FXLib.ColorOverLife(shock, FXLib.Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f },
                new[] { 1f, 0f }, new[] { 0f, 1f }));
            FXLib.Burst(shock, 1);
        }

        // ── 6. 물기둥 ─────────────────────────────────────────────────────
        if (nearSea) BuildWaterPlume(s);

        fire.Play(); smoke.Play(); spark.Play(); shock.Play();
    }

    void BuildWaterPlume(float s)
    {
        Vector3 surf = new Vector3(transform.position.x, FXLib.SeaY + 0.5f, transform.position.z);
        var col = FXLib.MakePS("WaterColumn", null, FXLib.Water, 50);
        col.transform.SetParent(transform, true);
        col.transform.position = surf;
        col.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // 원뿔이 위로
        {
            var m = col.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(18f * s, 42f * s);
            m.startSize     = new ParticleSystem.MinMaxCurve(3f * s, 6f * s);
            m.gravityModifier = 1.1f;
            var sh = col.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 2.5f * s;
            FXLib.SizeOverLife(col, FXLib.Curve(0f, 0.6f, 1f, 2.6f));
            FXLib.ColorOverLife(col, FXLib.Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f },
                new[] { 0f, 1f, 0f }, new[] { 0f, 0.08f, 1f }));
            FXLib.Burst(col, 40);
        }
        var mist = FXLib.MakePS("WaterMist", null, FXLib.Water, 24);
        mist.transform.SetParent(transform, true);
        mist.transform.position = surf;
        {
            var m = mist.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            m.startSpeed    = new ParticleSystem.MinMaxCurve(6f, 14f);
            m.startSize     = new ParticleSystem.MinMaxCurve(8f * s, 14f * s);
            var sh = mist.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 4f * s;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var lv = mist.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.2f; lv.limit = 1f;
            FXLib.SizeOverLife(mist, FXLib.Curve(0f, 0.5f, 1f, 2f));
            FXLib.ColorOverLife(mist, FXLib.Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f },
                new[] { 0f, 0.6f, 0f }, new[] { 0f, 0.15f, 1f }));
            FXLib.Burst(mist, 16);
        }
        col.Play(); mist.Play();
        SfxPlayer.Play(FlightSounds.WaterSplash, surf, 1f, 40f, 4000f);
    }

    void Update()
    {
        _t += Time.deltaTime;
        // 섬광: 0.04초 만에 최대 → 0.5초에 걸쳐 사그라듦 (불덩이 빛이 잔광으로 남음)
        float k = _t < 0.04f ? _t / 0.04f : Mathf.Exp(-(_t - 0.04f) * 7f) + 0.12f * Mathf.Exp(-_t * 1.8f);
        if (_light != null)
        {
            _light.intensity = 60f * _scale * k;
            if (k < 0.01f) { _light.enabled = false; _light = null; }   // URP 부속 컴포넌트 때문에 Destroy 불가 — 끄기만
        }
        _flash?.Set(new Color(1f, 0.8f, 0.55f), 14f * (_t < 0.04f ? 1f : Mathf.Exp(-(_t - 0.04f) * 14f)));
    }
}

/// <summary>폭발에서 튀어나와 포물선을 그리며 연기 꼬리를 끄는 파편</summary>
public class DebrisTrail : MonoBehaviour
{
    Vector3 _vel;
    float   _life, _age;
    ParticleSystem _smoke, _fire;

    public static void Spawn(Vector3 pos, Vector3 vel, float life)
    {
        var go = new GameObject("Debris");
        go.transform.position = pos;
        var d = go.AddComponent<DebrisTrail>();
        d._vel = vel; d._life = life;
        d.Build();
    }

    void Build()
    {
        _smoke = FXLib.MakePS("DebrisSmoke", transform, FXLib.Smoke, 450, worldSpace: true, loop: true);
        var m = _smoke.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        m.startSpeed    = 0.5f;
        m.startSize     = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var em = _smoke.emission; em.rateOverDistance = 3f;   // 크기보다 촘촘히 — 점선이 아니라 이어진 줄기
        // 불타는 파편 뒤로 회색 연기가 가늘게 시작해 퍼지며 옅어진다
        FXLib.SizeOverLife(_smoke, FXLib.Curve(0f, 0.6f, 1f, 3.5f));
        FXLib.ColorOverLife(_smoke, FXLib.Grad(new[] { new Color(0.45f, 0.42f, 0.4f), new Color(0.8f, 0.8f, 0.8f) }, new[] { 0f, 1f },
            new[] { 0.75f, 0.35f, 0f }, new[] { 0f, 0.35f, 1f }));

        _fire = FXLib.MakePS("DebrisFire", transform, FXLib.Glow, 60, worldSpace: true, loop: true);
        var fm = _fire.main;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        fm.startSpeed    = 0f;
        fm.startSize     = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        fm.startColor    = new Color(1f, 0.55f, 0.18f);
        var fe = _fire.emission; fe.rateOverDistance = 1.2f;
        FXLib.ColorOverLife(_fire, FXLib.Grad(new[] { Color.white, new Color(1f, 0.3f, 0.05f) }, new[] { 0f, 1f },
            new[] { 1f, 0f }, new[] { 0f, 1f }));
        FXLib.SizeOverLife(_fire, FXLib.Curve(0f, 1f, 1f, 0.3f));

        _smoke.Play(); _fire.Play();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _age += dt;
        _vel += Physics.gravity * dt;
        _vel *= 1f - Mathf.Min(1f, 0.9f * dt);   // 공기 저항 — 파편이 금방 감속해 아치형 궤적
        transform.position += _vel * dt;

        // 불은 먼저 꺼지고 연기만 남는다
        if (_age > _life * 0.55f && _fire != null && _fire.isEmitting) _fire.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        if (_age > _life || transform.position.y < FXLib.SeaY)
        {
            _smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            enabled = false;
            Destroy(gameObject, 4.5f);
        }
    }
}
