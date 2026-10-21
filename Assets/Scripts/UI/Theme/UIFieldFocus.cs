using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InputField 포커스 상태에 따라 테두리/배경색을 부드럽게 전환한다.
/// 입력 필드 GameObject에 붙이고 Init 으로 연결.
/// </summary>
public class UIFieldFocus : MonoBehaviour
{
    InputField _input;
    Image      _bg;
    Image      _outline;
    Image      _accent;
    Color      _bgCur, _olCur;
    float      _accentA;

    public void Init(InputField input, Image bg, Image outline, Image accent = null)
    {
        _input = input; _bg = bg; _outline = outline; _accent = accent;
        _bgCur = bg.color; _olCur = outline.color;
    }

    void Update()
    {
        if (_input == null) return;
        var t = UITheme.Instance;
        bool f = _input.isFocused;
        Color bgT = f ? new Color(t.panelBG.r + 0.02f, t.panelBG.g + 0.04f, t.panelBG.b + 0.07f, 0.85f)
                      : new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.62f);
        Color olT = f ? t.primaryBlue : new Color(t.border.r, t.border.g, t.border.b, 0.9f);
        float a = 1f - Mathf.Exp(-(1f / Mathf.Max(0.01f, t.hoverTransition)) * Time.unscaledDeltaTime);
        _bgCur = Color.Lerp(_bgCur, bgT, a);
        _olCur = Color.Lerp(_olCur, olT, a);
        _bg.color = _bgCur;
        _outline.color = _olCur;
        if (_accent != null)
        {
            _accentA = Mathf.Lerp(_accentA, f ? 1f : 0f, a);
            var c = t.accentCyan; c.a = _accentA;
            _accent.color = c;
        }
    }
}
