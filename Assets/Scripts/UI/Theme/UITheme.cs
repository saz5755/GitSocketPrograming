using UnityEngine;

/// <summary>
/// 전체 UI/UX의 디자인 토큰 중앙 관리.
/// 색상, 폰트, 간격, 모서리, 전환시간 등 공통 값을 한곳에서 조정.
/// Assets/Resources/UITheme.asset → Resources.Load로 전역 접근.
///
/// 디자인 콘셉트: Premium Military Flight Simulator
/// - Dark Navy / Aviation Blue / Cyan
/// - Subtle Glassmorphism
/// - Precise and Restrained Motion
/// </summary>
[CreateAssetMenu(menuName = "KF21/UI/UI Theme", fileName = "UITheme")]
public class UITheme : ScriptableObject
{
    // ── 전역 Instance ──────────────────────────────────────────────────────
    static UITheme _cached;
    public static UITheme Instance
    {
        get
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<UITheme>("UITheme");
            if (_cached == null)
            {
                if (Application.isPlaying)
                    Debug.LogWarning("[UITheme] Not found — using built-in defaults. " +
                                     "Create via Project 우클릭 → Create → KF21 → UI → UI Theme, " +
                                     "place at Assets/Resources/UITheme.asset");
                _cached = CreateInstance<UITheme>();
            }
            return _cached;
        }
    }

    // ── 색상 시스템 ────────────────────────────────────────────────────────
    [Header("배경")]
    [Tooltip("최하단 전역 배경 — 가장 어두운 네이비")]
    public Color primaryBG   = HexC("#06111F");
    [Tooltip("세컨더리 배경 — 섹션 구분")]
    public Color secondaryBG = HexC("#0A1B2D");
    [Tooltip("패널 배경 — 카드/모달")]
    public Color panelBG     = HexC("#0B2035");
    [Tooltip("글래스 반투명 오버레이 — 패널 위 Hover 등")]
    public Color glassTint   = new Color(0.17f, 0.47f, 0.97f, 0.08f);

    [Header("강조색")]
    [Tooltip("Primary — 주요 액션 버튼, 선택 상태")]
    public Color primaryBlue = HexC("#1677F8");
    [Tooltip("Accent — 하이라이트, 세부 포인트")]
    public Color accentCyan  = HexC("#46D7F2");

    [Header("텍스트")]
    [Tooltip("기본 텍스트 — 거의 흰색")]
    public Color textPrimary   = HexC("#F2F6FC");
    [Tooltip("보조 텍스트 — 설명, 플레이스홀더")]
    public Color textSecondary = HexC("#91A6BC");
    [Tooltip("비활성 텍스트")]
    public Color textDisabled  = HexC("#546B82");

    [Header("경계선")]
    [Tooltip("기본 테두리 — 차분한 블루그레이")]
    public Color border        = HexC("#24435F");
    [Tooltip("강조 테두리 — Focused/Selected")]
    public Color borderAccent  = HexC("#46D7F2");

    [Header("상태색")]
    [Tooltip("성공/긍정 (전투 성공, 완료)")]
    public Color success = HexC("#36C995");
    [Tooltip("경고 (저연료, 스톨 경고)")]
    public Color warning = HexC("#F4B84A");
    [Tooltip("에러 (피격, 치명)")]
    public Color error   = HexC("#F05B68");

    // ── 치수/간격 ───────────────────────────────────────────────────────────
    [Header("치수 (px)")]
    [Range(0f, 24f)] public float cornerRadius = 6f;    // (현재 UGUI엔 미사용, 참고용)
    [Range(1f, 4f)]  public float borderWidth  = 1.5f;
    [Range(0f, 48f)] public float spacingXS    = 4f;
    [Range(0f, 48f)] public float spacingS     = 8f;
    [Range(0f, 48f)] public float spacingM     = 16f;
    [Range(0f, 48f)] public float spacingL     = 24f;
    [Range(0f, 96f)] public float spacingXL    = 40f;

    [Header("버튼 높이")]
    [Range(28f, 72f)] public float buttonHeightS = 36f;
    [Range(28f, 72f)] public float buttonHeightM = 44f;
    [Range(28f, 72f)] public float buttonHeightL = 56f;

    [Header("폰트 크기")]
    [Range(10, 32)] public int textXS = 11;
    [Range(10, 32)] public int textS  = 13;
    [Range(10, 32)] public int textM  = 15;
    [Range(10, 48)] public int textL  = 20;
    [Range(14, 64)] public int titleM = 28;
    [Range(18, 96)] public int titleL = 44;

    // ── 전환 시간 (초) ─────────────────────────────────────────────────────
    [Header("전환 시간 (초)")]
    [Range(0.05f, 0.3f)] public float hoverTransition = 0.15f;
    [Range(0.03f, 0.2f)] public float pressTransition = 0.08f;
    [Range(0.1f, 0.5f)]  public float panelTransition = 0.22f;
    [Range(0.2f, 0.8f)]  public float screenTransition = 0.35f;
    [Range(0.3f, 1.5f)]  public float heroTransition  = 0.55f; // 배경/로고 등장

    // ── 유틸 ────────────────────────────────────────────────────────────────
    void OnValidate() { _cached = this; }

    static Color HexC(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return Color.magenta; // 눈에 띄는 폴백
    }

    /// <summary>버튼 Hover 상태 배경색 — 기본 + 글래스 틴트 가산.</summary>
    public Color HoverTint(Color baseColor)
    {
        return new Color(
            Mathf.Min(1f, baseColor.r + 0.08f),
            Mathf.Min(1f, baseColor.g + 0.10f),
            Mathf.Min(1f, baseColor.b + 0.12f),
            baseColor.a);
    }

    /// <summary>버튼 Pressed 상태 배경색 — 기본보다 약간 어둡게.</summary>
    public Color PressTint(Color baseColor)
    {
        return new Color(
            baseColor.r * 0.85f,
            baseColor.g * 0.85f,
            baseColor.b * 0.85f,
            baseColor.a);
    }
}
