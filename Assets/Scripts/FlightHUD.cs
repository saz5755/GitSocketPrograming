using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural flight HUD. Attach to any persistent GameObject in the scene.
/// Requires a Canvas set to Screen Space - Overlay (created automatically).
/// </summary>
public class FlightHUD : MonoBehaviour
{
    // ── HUD elements ──────────────────────────────────────────────────────
    Canvas canvas;
    RectTransform horizonRoot;   // rotates with roll
    RectTransform horizonSlide;  // slides up/down with pitch
    RectTransform skyPanel;
    RectTransform groundPanel;
    RectTransform pitchLadder;

    Text speedText;
    Text altText;
    Text headingText;
    Text vsiText;
    Text gforceText;

    // ── Runtime data ──────────────────────────────────────────────────────
    PlayerController localPlayer;

    Vector3 prevVelocity;
    float gforce = 1f;

    static readonly Color HudGreen = new Color(0f, 1f, 0.4f, 0.9f);

    // ── pitch ladder lines: degrees shown ────────────────────────────────
    static readonly int[] PitchMarks = { -30, -20, -10, -5, 5, 10, 20, 30 };
    const float PixelsPerDegree = 6f;

    void Start()
    {
        BuildHUD();
    }

    void FindLocalPlayer()
    {
        foreach (PlayerController pc in FindObjectsOfType<PlayerController>())
        {
            if (pc.isLocalPlayer) { localPlayer = pc; return; }
        }
    }

    void Update()
    {
        if (localPlayer == null) { FindLocalPlayer(); return; }
        UpdateHUD();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BUILD
    // ══════════════════════════════════════════════════════════════════════
    void BuildHUD()
    {
        // Canvas
        GameObject canvasGO = new GameObject("HUD_Canvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        BuildArtificialHorizon();
        BuildSpeedIndicator();
        BuildAltIndicator();
        BuildHeadingStrip();
        BuildVSI();
        BuildGForce();
        BuildCrosshair();
        BuildVignette();
    }

    // ── Artificial Horizon ────────────────────────────────────────────────
    void BuildArtificialHorizon()
    {
        // Mask so elements don't bleed outside the circle
        GameObject maskGO = new GameObject("AH_Mask");
        maskGO.transform.SetParent(canvas.transform, false);
        RectTransform maskRT = maskGO.AddComponent<RectTransform>();
        maskRT.sizeDelta = new Vector2(300, 300);
        maskRT.anchoredPosition = Vector2.zero;
        maskRT.anchorMin = maskRT.anchorMax = new Vector2(0.5f, 0.5f);
        Image maskImg = maskGO.AddComponent<Image>();
        maskImg.color = Color.clear;
        Mask mask = maskGO.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Sprite-less circle mask via RectTransform clip — use a square mask instead
        maskImg.color = new Color(0, 0, 0, 0.01f); // near-transparent to activate mask

        // horizonRoot: rotates with roll
        horizonRoot = MakeRect("AH_Root", maskGO.transform, Vector2.zero, new Vector2(300, 300));
        horizonRoot.anchorMin = horizonRoot.anchorMax = new Vector2(0.5f, 0.5f);

        // horizonSlide: child of root, slides with pitch
        horizonSlide = MakeRect("AH_Slide", horizonRoot, Vector2.zero, new Vector2(300, 600));
        horizonSlide.anchorMin = horizonSlide.anchorMax = new Vector2(0.5f, 0.5f);

        // Sky
        skyPanel = MakeRect("Sky", horizonSlide, new Vector2(0, 150), new Vector2(300, 300));
        skyPanel.anchorMin = skyPanel.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(skyPanel.gameObject, new Color(0.1f, 0.3f, 0.6f, 0.7f));

        // Ground
        groundPanel = MakeRect("Ground", horizonSlide, new Vector2(0, -150), new Vector2(300, 300));
        groundPanel.anchorMin = groundPanel.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(groundPanel.gameObject, new Color(0.4f, 0.25f, 0.05f, 0.7f));

        // Horizon line
        RectTransform horizLine = MakeRect("HLine", horizonSlide, Vector2.zero, new Vector2(300, 2));
        horizLine.anchorMin = horizLine.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(horizLine.gameObject, HudGreen);

        // Pitch ladder
        pitchLadder = MakeRect("PitchLadder", horizonSlide, Vector2.zero, new Vector2(300, 600));
        pitchLadder.anchorMin = pitchLadder.anchorMax = new Vector2(0.5f, 0.5f);
        foreach (int deg in PitchMarks)
        {
            float y = deg * PixelsPerDegree;
            float w = Mathf.Abs(deg) >= 10 ? 120 : 60;
            RectTransform line = MakeRect($"P{deg}", pitchLadder, new Vector2(0, y), new Vector2(w, 2));
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
            AddImage(line.gameObject, HudGreen);
            AddText(pitchLadder.gameObject, $"{deg}°", new Vector2(70, y), 10, TextAnchor.MiddleLeft);
        }

        // Wing markers
        RectTransform wL = MakeRect("WingL", horizonRoot, new Vector2(-80, 0), new Vector2(60, 3));
        wL.anchorMin = wL.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(wL.gameObject, HudGreen);
        RectTransform wR = MakeRect("WingR", horizonRoot, new Vector2(80, 0), new Vector2(60, 3));
        wR.anchorMin = wR.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(wR.gameObject, HudGreen);
        RectTransform center = MakeRect("WingC", horizonRoot, Vector2.zero, new Vector2(6, 6));
        center.anchorMin = center.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(center.gameObject, HudGreen);

        // Border circle (decorative square)
        RectTransform border = MakeRect("AH_Border", canvas.transform, Vector2.zero, new Vector2(304, 304));
        border.anchorMin = border.anchorMax = new Vector2(0.5f, 0.5f);
        Image bImg = border.gameObject.AddComponent<Image>();
        bImg.color = Color.clear;
        Outline outline = border.gameObject.AddComponent<Outline>();
        outline.effectColor = HudGreen;
        outline.effectDistance = new Vector2(2, 2);
    }

    // ── Speed indicator (left) ────────────────────────────────────────────
    void BuildSpeedIndicator()
    {
        GameObject panel = MakePanel("SpeedPanel", new Vector2(-330, 0), new Vector2(110, 200));
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        AddImage(panel, new Color(0, 0, 0, 0.5f));
        AddText(panel, "SPD", new Vector2(0, 80), 12, TextAnchor.MiddleCenter, HudGreen);
        speedText = AddText(panel, "0", new Vector2(0, 20), 28, TextAnchor.MiddleCenter, HudGreen);
        AddText(panel, "kph", new Vector2(0, -30), 11, TextAnchor.MiddleCenter, HudGreen);
    }

    // ── Altitude indicator (right) ────────────────────────────────────────
    void BuildAltIndicator()
    {
        GameObject panel = MakePanel("AltPanel", new Vector2(330, 0), new Vector2(110, 200));
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        AddImage(panel, new Color(0, 0, 0, 0.5f));
        AddText(panel, "ALT", new Vector2(0, 80), 12, TextAnchor.MiddleCenter, HudGreen);
        altText = AddText(panel, "0", new Vector2(0, 20), 28, TextAnchor.MiddleCenter, HudGreen);
        AddText(panel, "m", new Vector2(0, -30), 11, TextAnchor.MiddleCenter, HudGreen);
    }

    // ── Heading strip (top) ───────────────────────────────────────────────
    void BuildHeadingStrip()
    {
        GameObject panel = MakePanel("HDGPanel", new Vector2(0, 220), new Vector2(240, 50));
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        AddImage(panel, new Color(0, 0, 0, 0.5f));
        headingText = AddText(panel, "HDG 000°", new Vector2(0, 0), 18, TextAnchor.MiddleCenter, HudGreen);
    }

    // ── VSI (bottom-left) ─────────────────────────────────────────────────
    void BuildVSI()
    {
        GameObject panel = MakePanel("VSIPanel", new Vector2(-330, -220), new Vector2(130, 50));
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        AddImage(panel, new Color(0, 0, 0, 0.5f));
        vsiText = AddText(panel, "VSI +0 m/s", new Vector2(0, 0), 13, TextAnchor.MiddleCenter, HudGreen);
    }

    // ── G-force (bottom-right) ────────────────────────────────────────────
    void BuildGForce()
    {
        GameObject panel = MakePanel("GPanel", new Vector2(330, -220), new Vector2(130, 50));
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        AddImage(panel, new Color(0, 0, 0, 0.5f));
        gforceText = AddText(panel, "G  1.0", new Vector2(0, 0), 13, TextAnchor.MiddleCenter, HudGreen);
    }

    // ── Crosshair ─────────────────────────────────────────────────────────
    void BuildCrosshair()
    {
        Color c = new Color(HudGreen.r, HudGreen.g, HudGreen.b, 0.7f);

        RectTransform h = MakeRect("CH_H", canvas.transform, Vector2.zero, new Vector2(24, 2));
        h.anchorMin = h.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(h.gameObject, c);

        RectTransform v = MakeRect("CH_V", canvas.transform, Vector2.zero, new Vector2(2, 24));
        v.anchorMin = v.anchorMax = new Vector2(0.5f, 0.5f);
        AddImage(v.gameObject, c);
    }

    // ── Vignette ──────────────────────────────────────────────────────────
    void BuildVignette()
    {
        RectTransform vig = MakeRect("Vignette", canvas.transform, Vector2.zero, Vector2.zero);
        vig.anchorMin = Vector2.zero;
        vig.anchorMax = Vector2.one;
        vig.offsetMin = vig.offsetMax = Vector2.zero;

        Image img = vig.gameObject.AddComponent<Image>();
        // Radial vignette via sprite: use a solid black image with low alpha
        img.color = new Color(0, 0, 0, 0.35f);
        img.raycastTarget = false;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ══════════════════════════════════════════════════════════════════════
    Vector3 prevPos;
    bool prevPosSet;

    void UpdateHUD()
    {
        Transform t = localPlayer.transform;

        if (!prevPosSet) { prevPos = t.position; prevPosSet = true; }

        Vector3 euler = t.eulerAngles;
        float pitch = euler.x > 180 ? euler.x - 360 : euler.x;
        float roll  = euler.z > 180 ? euler.z - 360 : euler.z;
        float yaw   = euler.y;

        // Artificial horizon
        horizonRoot.localRotation      = Quaternion.Euler(0, 0, roll);
        horizonSlide.anchoredPosition  = new Vector2(0, pitch * PixelsPerDegree);

        // Velocity from position delta
        Vector3 vel   = (t.position - prevPos) / Time.deltaTime;
        float   speed = vel.magnitude * 3.6f;
        speedText.text   = $"{speed:F0}";
        altText.text     = $"{t.position.y:F0}";
        headingText.text = $"HDG {yaw:000}°";

        float vsi = vel.y;
        vsiText.text = $"VSI {(vsi >= 0 ? "+" : "")}{vsi:F1} m/s";

        // G-force
        Vector3 accel = (vel - prevVelocity) / Time.deltaTime;
        float g = (accel - Physics.gravity).magnitude / 9.81f;
        gforce = Mathf.Lerp(gforce, g, Time.deltaTime * 5f);
        gforceText.text = $"G  {gforce:F1}";

        prevVelocity = vel;
        prevPos      = t.position;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════
    RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    GameObject MakePanel(string name, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        return go;
    }

    void AddImage(GameObject go, Color color)
    {
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    void AddImage(RectTransform rt, Color color) => AddImage(rt.gameObject, color);

    Text AddText(GameObject parent, string content, Vector2 pos, int size,
                 TextAnchor anchor, Color? color = null)
    {
        GameObject go = new GameObject("Txt_" + content);
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(200, 40);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        Text txt = go.AddComponent<Text>();
        txt.text = content;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = size;
        txt.alignment = anchor;
        txt.color = color ?? HudGreen;
        txt.raycastTarget = false;
        return txt;
    }
}
