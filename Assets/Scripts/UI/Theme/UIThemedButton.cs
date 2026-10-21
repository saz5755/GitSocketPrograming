using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UITheme 기반 공통 버튼 컴포넌트.
/// Normal / Hover / Pressed / Disabled / Focused 상태 애니메이션 처리.
/// Image와 함께 작동 (RectTransform 자식 Text도 있으면 색상 자동 전환).
///
/// 사용법: Image가 붙은 GameObject에 AddComponent 후 Style 설정.
/// </summary>
[RequireComponent(typeof(Image))]
public class UIThemedButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                              IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
                              ISelectHandler, IDeselectHandler
{
    public enum Style { Primary, Secondary, Outline, Ghost, Danger }

    [SerializeField] Style _style = Style.Primary;
    [SerializeField] bool  _interactable = true;

    public System.Action OnClick;

    Image _bg;
    Text  _label;
    Image _outline;      // 둥근 테두리 링 (UIShapes)
    RectTransform _rt;

    bool  _hovered;
    bool  _pressed;
    bool  _focused;
    bool  _selected;
    bool  _started;      // Start 이전의 스타일/상태 변경은 즉시 반영 (첫 프레임 색 번쩍임 방지)
    Color _targetBG;
    Color _targetTxt;
    Color _targetOutline;
    float _targetScale = 1f;

    Color _curBG;
    Color _curTxt;
    Color _curOutline;
    float _curScale = 1f;

    void Awake()
    {
        _bg      = GetComponent<Image>();
        _label   = GetComponentInChildren<Text>();
        _rt      = (RectTransform)transform;

        // 둥근 모서리 + 테두리 링. 호출 측이 붙여 둔 Outline 은 링으로 대체한다.
        int radius = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(_rt.rect.height > 1f ? _rt.rect.height : _rt.sizeDelta.y, 56f) * 0.2f), 6, 12);
        if (_bg.sprite == null) UIShapes.Round(_bg, radius);
        var oldOutline = GetComponent<Outline>();
        if (oldOutline != null) { oldOutline.enabled = false; Destroy(oldOutline); }
        _outline = UIShapes.AddBorder(transform, radius, Color.clear);
        _outline.transform.SetAsFirstSibling();
        if (_label != null) _label.transform.SetAsLastSibling();
        ApplyTargetsInstant();
    }

    void Start() { _started = true; }

    void OnEnable()
    {
        _hovered = _pressed = false;
        UpdateTargets();
        ApplyTargetsInstant();
    }

    public Style ButtonStyle
    {
        get => _style;
        set { _style = value; UpdateTargets(); if (!_started) ApplyTargetsInstant(); }
    }

    public bool Interactable
    {
        get => _interactable;
        set { _interactable = value; UpdateTargets(); if (!_started) ApplyTargetsInstant(); }
    }

    /// <summary>메뉴/탭에서 현재 선택된 항목 표시.</summary>
    public bool Selected
    {
        get => _selected;
        set { _selected = value; UpdateTargets(); }
    }

    public void SetLabel(string txt)
    {
        if (_label != null) _label.text = txt;
    }

    // ── 상태 변경 콜백 ──────────────────────────────────────────────────────
    public void OnPointerEnter(PointerEventData e) { _hovered = true;  UpdateTargets(); }
    public void OnPointerExit (PointerEventData e) { _hovered = false; _pressed = false; UpdateTargets(); }
    public void OnPointerDown (PointerEventData e) { if (_interactable) { _pressed = true;  UpdateTargets(); } }
    public void OnPointerUp   (PointerEventData e) { _pressed = false; UpdateTargets(); }
    public void OnPointerClick(PointerEventData e) { if (_interactable) OnClick?.Invoke(); }
    public void OnSelect      (BaseEventData e)    { _focused = true;  UpdateTargets(); }
    public void OnDeselect    (BaseEventData e)    { _focused = false; UpdateTargets(); }

    // ── 상태별 색상 계산 ───────────────────────────────────────────────────
    void UpdateTargets()
    {
        var t = UITheme.Instance;
        Color baseBG = BaseBGForStyle(t);
        Color baseTxt = BaseTxtForStyle(t);
        Color baseOutline = BaseOutlineForStyle(t);
        if (_selected)
        {
            baseBG = new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.30f);
            baseTxt = t.textPrimary;
            baseOutline = t.accentCyan;
        }

        if (!_interactable)
        {
            _targetBG = DimColor(baseBG, 0.5f);
            _targetTxt = t.textDisabled;
            _targetOutline = DimColor(baseOutline, 0.5f);
            _targetScale = 1f;
        }
        else if (_pressed)
        {
            _targetBG = baseBG.a < 0.1f ? FillForTransparent(t, 0.14f) : t.PressTint(baseBG);
            _targetTxt = baseTxt;
            _targetOutline = t.borderAccent;
            _targetScale = 0.97f;
        }
        else if (_hovered || _focused)
        {
            _targetBG = baseBG.a < 0.1f ? FillForTransparent(t, 0.08f) : t.HoverTint(baseBG);
            _targetTxt = _style == Style.Ghost ? t.textPrimary : baseTxt;
            _targetOutline = t.borderAccent;
            _targetScale = 1.015f;
        }
        else
        {
            _targetBG = baseBG;
            _targetTxt = baseTxt;
            _targetOutline = baseOutline;
            _targetScale = 1f;
        }
    }

    Color BaseBGForStyle(UITheme t) => _style switch
    {
        Style.Primary   => t.primaryBlue,
        Style.Secondary => new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.12f),
        Style.Outline   => new Color(0f, 0f, 0f, 0f),
        Style.Ghost     => new Color(1f, 1f, 1f, 0f),
        Style.Danger    => new Color(t.error.r, t.error.g, t.error.b, 0.85f),
        _ => t.panelBG,
    };

    Color BaseTxtForStyle(UITheme t) => _style switch
    {
        Style.Primary   => t.textPrimary,
        Style.Secondary => t.textPrimary,
        Style.Outline   => t.accentCyan,
        Style.Ghost     => t.textSecondary,
        Style.Danger    => t.textPrimary,
        _ => t.textPrimary,
    };

    Color BaseOutlineForStyle(UITheme t) => _style switch
    {
        Style.Primary   => new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.0f),
        Style.Secondary => t.border,
        Style.Outline   => t.accentCyan,
        Style.Ghost     => new Color(0f, 0f, 0f, 0f),
        Style.Danger    => new Color(0f, 0f, 0f, 0f),
        _ => t.border,
    };

    // 투명 배경 스타일(Ghost/Outline)의 호버·눌림 채움색
    Color FillForTransparent(UITheme t, float alpha)
        => _style == Style.Outline ? new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, alpha)
                                   : new Color(1f, 1f, 1f, alpha);

    static Color DimColor(Color c, float m)
        => new Color(c.r * m, c.g * m, c.b * m, c.a * (0.5f + 0.5f * m));

    // ── 보간 (매 프레임) ───────────────────────────────────────────────────
    void Update()
    {
        var t = UITheme.Instance;
        float lerpSpeed = _pressed ? (1f / Mathf.Max(0.01f, t.pressTransition))
                                   : (1f / Mathf.Max(0.01f, t.hoverTransition));
        float a = 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime);

        _curBG      = Color.Lerp(_curBG, _targetBG, a);
        _curTxt     = Color.Lerp(_curTxt, _targetTxt, a);
        _curOutline = Color.Lerp(_curOutline, _targetOutline, a);
        _curScale   = Mathf.Lerp(_curScale, _targetScale, a);

        if (_bg != null)      _bg.color = _curBG;
        if (_label != null)   _label.color = _curTxt;
        if (_outline != null) _outline.color = _curOutline;
        if (_rt != null)      _rt.localScale = new Vector3(_curScale, _curScale, 1f);
    }

    void ApplyTargetsInstant()
    {
        _curBG      = _targetBG;
        _curTxt     = _targetTxt;
        _curOutline = _targetOutline;
        _curScale   = _targetScale;
        if (_bg != null)      _bg.color = _curBG;
        if (_label != null)   _label.color = _curTxt;
        if (_outline != null) _outline.color = _curOutline;
        if (_rt != null)      _rt.localScale = new Vector3(_curScale, _curScale, 1f);
    }
}
