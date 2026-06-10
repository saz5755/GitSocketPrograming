using UnityEngine;

// 엔진 배기 VFX 프리팹의 루트에 부착하는 어셈블리 컴포넌트.
// 아티스트가 Unity 에디터에서 ParticleSystem 자식들과 Light를 자유롭게 배치한 후,
// 슬롯에 드래그해서 등록하면 F35VFXController / EscortVFXController가 통일된 API로 제어한다.
//
// 사용 흐름:
//  1. 빈 GameObject 생성 → EngineExhaustAssembly 부착
//  2. 자식으로 외부 글로우 ParticleSystem, 내부 코어 ParticleSystem, Point Light 배치
//  3. Inspector에서 각각의 슬롯에 드래그
//  4. 이 GameObject를 프리팹으로 저장 (예: F35_AfterburnerVFX.prefab)
//  5. F35VFXController._afterburnerVFXPrefab 슬롯에 프리팹 드래그
public class EngineExhaustAssembly : MonoBehaviour
{
    [Header("파티클")]
    [Tooltip("외부 글로우 (메인 화염)")]
    [SerializeField] ParticleSystem _outerGlow;
    [Tooltip("내부 쇼크 다이아몬드 (코어)")]
    [SerializeField] ParticleSystem _innerCore;

    [Header("광원")]
    [SerializeField] Light _light;
    [Tooltip("VFX 활성 시 최대 광도")]
    [SerializeField] float _maxLightIntensity = 5f;
    [Tooltip("광도 보간 속도 (Lerp factor — 클수록 즉시 반응)")]
    [SerializeField] float _lightLerpSpeed = 10f;

    void Awake()
    {
        // 처음엔 모두 꺼져 있어야 함 — playOnAwake 무시 보정
        if (_outerGlow != null) _outerGlow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_innerCore != null) _innerCore.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_light     != null) _light.intensity = 0f;
    }

    // 절차적으로 빌드한 컴포넌트를 등록할 때 사용 — F35VFXController/EscortVFXController fallback 경로
    public void Configure(ParticleSystem outerGlow, ParticleSystem innerCore, Light light,
                          float maxLightIntensity = 5f, float lightLerpSpeed = 10f)
    {
        _outerGlow         = outerGlow;
        _innerCore         = innerCore;
        _light             = light;
        _maxLightIntensity = maxLightIntensity;
        _lightLerpSpeed    = lightLerpSpeed;
    }

    // F35VFXController/EscortVFXController가 매 프레임 호출.
    // dt는 Time.deltaTime — Lerp 시 사용.
    public void SetActive(bool active, float dt)
    {
        if (active)
        {
            if (_outerGlow != null && !_outerGlow.isPlaying) _outerGlow.Play();
            if (_innerCore != null && !_innerCore.isPlaying) _innerCore.Play();
            if (_light != null)
                _light.intensity = Mathf.Lerp(_light.intensity, _maxLightIntensity, dt * _lightLerpSpeed);
        }
        else
        {
            if (_outerGlow != null && _outerGlow.isPlaying) _outerGlow.Stop();
            if (_innerCore != null && _innerCore.isPlaying) _innerCore.Stop();
            if (_light != null)
                _light.intensity = Mathf.Lerp(_light.intensity, 0f, dt * _lightLerpSpeed);
        }
    }
}
