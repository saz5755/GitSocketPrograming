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
    public enum Style { Primary, Secondary, Outline, Ghost }

    [SerializeField] Style _style = Style.Primary;
    [SerializeField] bool  _interactable = true;

    public System.Action OnClick;

    Image _bg;
    Text  _label;
    Outline _outline;
    RectTransform _rt;

    bool  _hovered;
    bool  _pressed;
    bool  _focused;
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
        _outline = GetComponent<Outline>();
        _rt      = (RectTransform)transform;
        ApplyTargetsInstant();
    }

    void OnEnable()
    {
        _hovered = _pressed = false;
        UpdateTargets();
        ApplyTargetsInstant();
    }

    public Style ButtonStyle
    {
        get => _style;
        set { _style = value; UpdateTargets(); }
    }

    public bool Interactable
    {
        get => _interactable;
        set { _interactable = value; UpdateTargets(); }
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

        if (!_interactable)
        {
            _targetBG = DimColor(baseBG, 0.5f);
            _targetTxt = t.textDisabled;
            _targetOutline = DimColor(baseOutline, 0.5f);
            _targetScale = 1f;
        }
        else if (_pressed)
        {
            _targetBG = t.PressTint(baseBG);
            _targetTxt = baseTxt;
            _targetOutline = t.borderAccent;
            _targetScale = 0.97f;
        }
        else if (_hovered || _focused)
        {
            _targetBG = t.HoverTint(baseBG);
            _targetTxt = baseTxt;
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
        Style.Secondary => t.panelBG,
        Style.Outline   => new Color(0f, 0f, 0f, 0f),
        Style.Ghost     => new Color(1f, 1f, 1f, 0.04f),
        _ => t.panelBG,
    };

    Color BaseTxtForStyle(UITheme t) => _style switch
    {
        Style.Primary   => t.textPrimary,
        Style.Secondary => t.textPrimary,
        Style.Outline   => t.accentCyan,
        Style.Ghost     => t.textSecondary,
        _ => t.textPrimary,
    };

    Color BaseOutlineForStyle(UITheme t) => _style switch
    {
        Style.Primary   => new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.0f),
        Style.Secondary => t.border,
        Style.Outline   => t.accentCyan,
        Style.Ghost     => new Color(0f, 0f, 0f, 0f),
        _ => t.border,
    };

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
        if (_outline != null) _outline.effectColor = _curOutline;
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
        if (_outline != null) _outline.effectColor = _curOutline;
        if (_rt != null)      _rt.localScale = new Vector3(_curScale, _curScale, 1f);
    }
}
