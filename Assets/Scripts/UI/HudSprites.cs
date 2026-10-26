using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 콕핏 HUD 이미지 키트 (Assets/Resources/HUD/*.png — 흰색+알파, Image.color 로 틴트).
/// 임포트 설정(9-slice 테두리 등)은 Editor/HudSpriteImporter 가 지정한다.
/// 에셋 스프라이트라 씬에 베이크해도 참조가 유지된다 (런타임 생성 UIShapes 스프라이트와 다른 점).
/// </summary>
public static class HudSprites
{
    public const string Panel       = "hud_panel";
    public const string PanelLine   = "hud_panel_line";
    public const string PanelAccent = "hud_panel_accent";
    public const string Pill        = "hud_pill";
    public const string PillLine    = "hud_pill_line";
    public const string Corner      = "hud_corner";
    public const string Ring        = "hud_ring";
    public const string RingThin    = "hud_ring_thin";
    public const string Disc        = "hud_disc";
    public const string Glow        = "hud_glow";
    public const string Rwr         = "hud_rwr";
    public const string MarkerBox   = "hud_marker_box";
    public const string MarkerTri   = "hud_marker_tri";
    public const string Diamond     = "hud_diamond";
    public const string Chevron     = "hud_chevron";
    public const string Vignette    = "hud_vignette";
    public const string SegBar      = "hud_segbar";
    public const string Missile     = "hud_missile";
    public const string GlowLine    = "hud_glowline";
    public const string Fpv         = "hud_fpv";
    public const string Waterline   = "hud_waterline";
    public const string Caret       = "hud_caret";
    public const string Readout     = "hud_readout";
    public const string ReadoutFill = "hud_readout_fill";
    public const string Dash        = "hud_dash";
    public const string Sheen       = "hud_sheen";

    static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

    public static Sprite Get(string name)
    {
        if (_cache.TryGetValue(name, out var s) && s != null) return s;
        s = Resources.Load<Sprite>("HUD/" + name);
        if (s == null) Debug.LogWarning($"[HudSprites] missing sprite: HUD/{name}");
        _cache[name] = s;
        return s;
    }
}
