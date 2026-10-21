using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 배경 사진을 원본 비율 그대로 부모(캔버스) 크기에 맞춰 배치한다. 어떤 해상도/비율에서도 늘어나지 않는다.
/// Cover      : 화면을 꽉 채우고 넘치는 부분은 잘라낸다 (중앙 기준, focus로 가로 위치 조정).
/// FitHeight  : 높이에 맞추고 우측 정렬.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class UIBackdropFit : MonoBehaviour
{
    public enum Mode { Cover, FitHeightRight }

    [SerializeField] Mode _mode = Mode.Cover;
    [SerializeField] float _aspect = 1.78f;
    // Cover 에서 가로로 넘칠 때 어느 쪽을 보여줄지 (0=좌, 0.5=중앙, 1=우)
    [SerializeField, Range(0f, 1f)] float _focusX = 0.5f;

    RectTransform _rt;
    Vector2 _lastParent = new Vector2(-1, -1);

    public void Configure(Mode mode, float aspect, float focusX = 0.5f)
    {
        _mode = mode; _aspect = Mathf.Max(0.1f, aspect); _focusX = focusX;
        _lastParent = new Vector2(-1, -1);
        Apply();
    }

    void OnEnable() { _lastParent = new Vector2(-1, -1); }

    void LateUpdate() { Apply(); }

    void Apply()
    {
        if (_rt == null) _rt = (RectTransform)transform;
        var parent = _rt.parent as RectTransform;
        if (parent == null) return;
        Vector2 p = parent.rect.size;
        if (p.x <= 0f || p.y <= 0f || p == _lastParent) return;
        _lastParent = p;

        float w, h;
        if (_mode == Mode.FitHeightRight) { h = p.y; w = h * _aspect; }
        else if (p.x / p.y > _aspect)     { w = p.x; h = w / _aspect; }
        else                               { h = p.y; w = h * _aspect; }

        if (_mode == Mode.FitHeightRight)
        {
            _rt.anchorMin = _rt.anchorMax = new Vector2(1f, 0.5f);
            _rt.pivot = new Vector2(1f, 0.5f);
            _rt.anchoredPosition = Vector2.zero;
        }
        else
        {
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.anchoredPosition = new Vector2((0.5f - _focusX) * Mathf.Max(0f, w - p.x), 0f);
        }
        _rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>알파 그라데이션 RawImage 생성. horizontal: 좌(aStart)→우(aEnd), 아니면 하→상.</summary>
    public static RectTransform MakeGradient(Transform parent, string name, bool horizontal,
                                             Color color, float aStart, float aEnd)
    {
        const int N = 128;
        var tex = new Texture2D(horizontal ? N : 1, horizontal ? 1 : N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < N; i++)
        {
            float u = i / (float)(N - 1);
            u = u * u * (3f - 2f * u);
            var c = new Color(color.r, color.g, color.b, Mathf.Lerp(aStart, aEnd, u));
            if (horizontal) tex.SetPixel(i, 0, c); else tex.SetPixel(0, i, c);
        }
        tex.Apply();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var raw = go.AddComponent<RawImage>();
        raw.texture = tex; raw.raycastTarget = false;
        return rt;
    }
}
