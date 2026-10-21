using System.Collections;
using UnityEngine;

/// <summary>
/// CanvasGroup 기반 Fade In + 미세 상승 이동. 패널 순차 등장용.
/// 사용법: 패널 GameObject에 AddComponent, delay/duration 설정.
/// CanvasGroup 없으면 자동 추가.
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
    Vector2 _origAnchoredPos;

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        _rt = transform as RectTransform;
    }

    void OnEnable()
    {
        if (_rt != null) _origAnchoredPos = _rt.anchoredPosition;
        _cg.alpha = 0f;
        if (_rt != null) _rt.anchoredPosition = _origAnchoredPos + Vector2.down * _riseDistance;
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
        if (_delay > 0f) yield return new WaitForSecondsRealtime(_delay);

        float d = _duration > 0f ? _duration : UITheme.Instance.panelTransition;
        float t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / d);
            // easeOutCubic
            float e = 1f - Mathf.Pow(1f - u, 3f);
            _cg.alpha = e;
            if (_rt != null)
                _rt.anchoredPosition = Vector2.Lerp(
                    _origAnchoredPos + Vector2.down * _riseDistance,
                    _origAnchoredPos, e);
            yield return null;
        }
        _cg.alpha = 1f;
        if (_rt != null) _rt.anchoredPosition = _origAnchoredPos;
    }
}
