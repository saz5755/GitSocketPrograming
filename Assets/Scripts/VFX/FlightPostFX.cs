using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 비행 화면 포스트 프로세싱 — 상용 비행시뮬의 '시네마틱하지만 과하지 않은' 룩.
///   · ACES 톤매핑 + 블룸 (HDR 화염·섬광·예광탄·항법등이 자연스럽게 번짐)
///   · 스크린 스페이스 렌즈 플레어 — 태양·폭발 섬광의 은은한 광선
///   · 약한 콘트라스트·채도 보정, 기본 비네트
///   · 동적: G 스트레스에 따라 비네트가 조여 들고, 큰 충격에 색수차가 잠깐 번진다
/// FlightCamera 가 Awake 에서 붙인다. 로비 HoloStage 볼륨(priority 50)보다 낮은 우선순위.
/// </summary>
[RequireComponent(typeof(Camera))]
public class FlightPostFX : MonoBehaviour
{
    [Header("기본 룩")]
    [SerializeField] float bloomIntensity   = 0.6f;
    [SerializeField] float bloomThreshold   = 1.05f;
    [SerializeField] float contrast         = 8f;
    [SerializeField] float saturation       = 6f;
    [SerializeField] float baseVignette     = 0.18f;
    [SerializeField] float lensFlare        = 0.35f;

    Volume _vol;
    VolumeProfile _prof;
    Vignette _vig;
    ChromaticAberration _ca;
    float _caPulse;

    static FlightPostFX s_inst;

    /// <summary>큰 충격 — 색수차 펄스 (피격·근접 폭발)</summary>
    public static void Impact(float amount) { if (s_inst != null) s_inst._caPulse = Mathf.Max(s_inst._caPulse, amount); }

    void Awake()
    {
        s_inst = this;
        var cam = GetComponent<Camera>();
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = true;
            // MSAA 가 꺼진 설정이면 예광탄·항적 같은 가는 선이 계단지므로 SMAA 를 켠다 (이미 지정돼 있으면 존중)
            if (data.antialiasing == AntialiasingMode.None)
            {
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.Medium;
            }
        }

        _prof = ScriptableObject.CreateInstance<VolumeProfile>();
        _prof.name = "FlightPostFX (runtime)";
        _prof.hideFlags = HideFlags.DontSave;

        var tm = _prof.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);

        var bloom = _prof.Add<Bloom>(true);
        bloom.intensity.Override(bloomIntensity);
        bloom.threshold.Override(bloomThreshold);
        bloom.scatter.Override(0.68f);
        bloom.highQualityFiltering.Override(true);
        bloom.tint.Override(new Color(1f, 0.96f, 0.9f));

        var ca = _prof.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.15f);   // ACES 가 중간톤을 약간 어둡게 누르는 것 보정
        ca.contrast.Override(contrast);
        ca.saturation.Override(saturation);

        var wb = _prof.Add<WhiteBalance>(true);
        wb.temperature.Override(3f);       // 약간 따뜻하게 — 바다·하늘의 청색이 차갑게 뜨지 않게

        _vig = _prof.Add<Vignette>(true);
        _vig.intensity.Override(baseVignette);
        _vig.smoothness.Override(0.45f);
        _vig.color.Override(Color.black);

        _ca = _prof.Add<ChromaticAberration>(true);
        _ca.intensity.Override(0f);

        var lf = _prof.Add<ScreenSpaceLensFlare>(true);
        lf.intensity.Override(lensFlare);
        lf.firstFlareIntensity.Override(0.6f);
        lf.secondaryFlareIntensity.Override(0.4f);
        lf.warpedFlareIntensity.Override(0.3f);
        lf.streaksIntensity.Override(0.25f);
        lf.streaksLength.Override(0.35f);
        lf.tintColor.Override(new Color(1f, 0.92f, 0.82f));

        var go = new GameObject("[FlightPostFX Volume]");
        go.transform.SetParent(transform, false);
        _vol = go.AddComponent<Volume>();
        _vol.isGlobal = true;
        _vol.priority = 5;
        _vol.sharedProfile = _prof;
    }

    void OnDestroy()
    {
        if (s_inst == this) s_inst = null;
        if (_prof != null) Destroy(_prof);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var gm = GameModeManager.Instance;
        bool flying = gm != null && gm.IsFlying && !gm.IsDead;
        var gloc = GLocSystem.Instance;
        float stress = flying && gloc != null ? Mathf.Clamp01(Mathf.Max(gloc.Stress, gloc.NegStress)) : 0f;

        // G 스트레스: 시야가 가장자리부터 좁아짐 (GLocSystem 터널 연출의 앞단계 — 은은하게)
        float vigT = baseVignette + stress * 0.22f;
        _vig.intensity.value = Mathf.Lerp(_vig.intensity.value, vigT, 1f - Mathf.Exp(-dt * 4f));

        _caPulse = Mathf.MoveTowards(_caPulse, 0f, dt * 1.5f);
        // 평소엔 0 — 가는 항적·예광탄에 색 테두리가 생기지 않게. 충격·G 스트레스 때만
        _ca.intensity.value = _caPulse * 0.6f + stress * 0.08f;
    }
}
