using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Resources/HUD/*.png 를 HUD 스프라이트로 임포트 (흰색+알파, Image.color 로 틴트).
/// 9-slice 가 필요한 패널류는 여기서 테두리를 지정한다.
/// </summary>
public class HudSpriteImporter : AssetPostprocessor
{
    const string Folder = "Assets/Resources/HUD/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.mipmapEnabled       = false;
        ti.alphaIsTransparency = true;
        ti.sRGBTexture         = true;
        ti.wrapMode            = assetPath.EndsWith("hud_dash.png") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.filterMode          = FilterMode.Bilinear;
        ti.textureCompression  = TextureImporterCompression.Uncompressed;
        ti.spritePixelsPerUnit = 100;
        ti.spriteBorder        = Border(System.IO.Path.GetFileNameWithoutExtension(assetPath));
    }

    // (left, bottom, right, top)
    static Vector4 Border(string name) => name switch
    {
        "hud_panel"        => new Vector4(22, 22, 22, 22),
        "hud_panel_line"   => new Vector4(22, 22, 22, 22),
        "hud_panel_accent" => new Vector4(22, 22, 22, 22),
        "hud_pill"         => new Vector4(16, 16, 16, 16),
        "hud_pill_line"    => new Vector4(16, 16, 16, 16),
        "hud_readout"      => new Vector4(8, 8, 18, 8),
        "hud_readout_fill" => new Vector4(8, 8, 18, 8),
        _                  => Vector4.zero,
    };
}
