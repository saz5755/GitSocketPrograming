using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space nickname label. Add this MonoBehaviour to the player root;
/// it creates its own child Canvas so the root is unaffected.
/// </summary>
public class PlayerLabel : MonoBehaviour
{
    Text label;
    Transform cam;

    public void SetNickname(string nickname)
    {
        if (label != null) label.text = nickname;
    }

    void Awake()
    {
        GameObject canvasGO = new GameObject("LabelCanvas");
        canvasGO.transform.SetParent(transform, false);

        Canvas c = canvasGO.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;

        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(2f, 0.4f);
        crt.localPosition = new Vector3(0, 2f, 0);
        crt.localScale = Vector3.one * 0.01f;

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(canvasGO.transform, false);
        RectTransform trt = textGO.AddComponent<RectTransform>();
        trt.sizeDelta = new Vector2(200, 40);
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = Vector2.zero;

        label = textGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 24;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0f, 1f, 0.4f);
    }

    void Start()
    {
        cam = Camera.main?.transform;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main?.transform;
        if (cam != null && label != null)
            label.transform.parent.forward = cam.forward;
    }
}
