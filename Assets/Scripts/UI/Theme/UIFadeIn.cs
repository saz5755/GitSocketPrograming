using System.Collections;
using UnityEngine;

/// <summary>
/// CanvasGroup 기반 Fade In + 미세 상승 이동. 패널 순차 등장용.
/// 사용법: 패널 GameObject에 AddComponent 후 Configure(delay, duration, rise).
/// CanvasGroup 없으면 자동 추가. rise 가 0 이면 위치는 건드리지 않는다 (레이아웃 그룹/Drift 와 공존 가능).
/// </summary>
public class UIFadeIn : MonoBehaviour
{
    [Tooltip("등장 지연 시간 (초)")]
    [SerializeField] float _delay = 0f;
    [Tooltip("등장 지속 시간 (초, 미설정 시 UITheme.panelTransition)")]
    [SerializeField] float _duration = -1f;
    [Tooltip("상승 거리 (아래→위, px). 0이면 알파만")]
    [SerializeField] float _riseDistance = 12f;

    CanvasGroup _cg;
    RectTransform _rt;

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        _rt = transform as RectTransform;
    }

    void OnEnable()
    {
        // Configure()는 AddComponent 직후에 호출되므로, 값은 코루틴의 첫 프레임 뒤에 읽는다
        _cg.alpha = 0f;
        StartCoroutine(FadeCo());
    }

    public void Configure(float delay, float duration = -1f, float riseDistance = 12f)
    {
        _delay = delay;
        _duration = duration;
        _riseDistance = riseDistance;
    }

    IEnumerator FadeCo()
    {
        yield return UIStartup.WaitStable();   // 씬 시작 직후 정지 구간이 끝난 뒤에 애니메이션 시작
        if (_delay > 0f) yield return new WaitForSecondsRealtime(_delay);

        bool rise = _rt != null && _riseDistance > 0.01f;
        Vector2 orig = rise ? _rt.anchoredPosition : Vector2.zero;
        float d = _duration > 0f ? _duration : UITheme.Instance.panelTransition;
        float t = 0f;
        while (t < d)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);   // 프레임이 튀어도 애니메이션이 점프하지 않게
            float u = Mathf.Clamp01(t / d);
            float e = 1f - Mathf.Pow(1f - u, 3f);   // easeOutCubic
            _cg.alpha = e;
            if (rise) _rt.anchoredPosition = orig + Vector2.down * _riseDistance * (1f - e);
            yield return null;
        }
        _cg.alpha = 1f;
        if (rise) _rt.anchoredPosition = orig;
    }
}
