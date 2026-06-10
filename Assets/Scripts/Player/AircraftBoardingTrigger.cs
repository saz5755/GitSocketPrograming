using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 월드에 배치된 항공기에 부착.
/// 인간형 플레이어가 boardingRadius 이내에 들어오면 원형 E키 UI를 표시하고
/// GameModeManager에 탑승 가능 상태를 알린다.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class AircraftBoardingTrigger : MonoBehaviour
{
    public static readonly System.Collections.Generic.List<AircraftBoardingTrigger> All = new();

    [SerializeField] float boardingRadius = 8f;

    PlayerController _pc;
    Canvas           _worldCanvas;
    bool             _playerInRange;

    public PlayerController Aircraft    => _pc;
    // 다른 플레이어가 콕핏 탑승 중이면 true — PlayerManager가 설정
    public bool             IsOccupied  { get; set; }

    void Awake()
    {
        All.Add(this);
        _pc = GetComponent<PlayerController>();
        _pc.enabled = false;   // 비행 시작 전까지 비활성

        BuildBoardingUI();
        SetUIVisible(false);
    }

    void OnDestroy() => All.Remove(this);

    void OnDisable()
    {
        SetUIVisible(false);
        if (_playerInRange)
        {
            _playerInRange = false;
            GameModeManager.Instance?.ClearBoardingTarget(this);
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        var gm = GameModeManager.Instance;
        if (gm == null || gm.GroundCharacter == null)
        {
            if (_playerInRange) Leave(gm);
            return;
        }

        // 다른 플레이어가 탑승 중(READY 상태)이면 이 항공기의 탑승 UI 완전 차단
        if (IsOccupied)
        {
            if (_playerInRange) Leave(gm);
            SetUIVisible(false);
            return;
        }

        // 자신이 탑승 중이거나 비행 중이면 UI 숨김 + 범위 상태 리셋
        // (하차 후 다시 범위 내에 있을 때 Enter()가 재발동되어 UI가 즉시 복원됨)
        if (gm.IsBoardedInCockpit || gm.IsFlying)
        {
            if (_playerInRange)
            {
                _playerInRange = false;
                gm.ClearBoardingTarget(this);
            }
            SetUIVisible(false);
            return;
        }

        float distSq  = (gm.GroundCharacter.position - transform.position).sqrMagnitude;
        bool  inRange = distSq <= boardingRadius * boardingRadius;

        if (inRange  && !_playerInRange) Enter(gm);
        if (!inRange &&  _playerInRange) Leave(gm);

        // Canvas +Z가 카메라 반대 방향을 향해야 정면(비미러)이 보임
        if (_playerInRange && _worldCanvas != null && Camera.main != null)
        {
            Vector3 toCamera = Camera.main.transform.position - _worldCanvas.transform.position;
            _worldCanvas.transform.rotation = Quaternion.LookRotation(-toCamera);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Enter(GameModeManager gm)
    {
        _playerInRange = true;
        SetUIVisible(true);
        gm.SetBoardingTarget(this);
    }

    void Leave(GameModeManager gm)
    {
        _playerInRange = false;
        SetUIVisible(false);
        gm?.ClearBoardingTarget(this);
    }

    void SetUIVisible(bool v) { if (_worldCanvas != null) _worldCanvas.enabled = v; }

    // ── 원형 E키 UI 빌드 ─────────────────────────────────────────────────────
    void BuildBoardingUI()
    {
        var go = new GameObject("AircraftBoardingUI");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 3.5f, 0f);
        go.transform.localScale    = Vector3.one * 0.018f;

        _worldCanvas = go.AddComponent<Canvas>();
        _worldCanvas.renderMode   = RenderMode.WorldSpace;
        _worldCanvas.sortingOrder = 10;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(140f, 140f);

        // 채워진 원 배경 (반투명 검정)
        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(go.transform, false);
        SetRect(bgGO, Vector2.zero, Vector2.one);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.sprite = CreateFilledCircleSprite(64);
        bgImg.color  = new Color(0f, 0f, 0f, 0.70f);

        // 흰색 링 테두리
        var ringGO = new GameObject("Ring");
        ringGO.transform.SetParent(go.transform, false);
        SetRect(ringGO, Vector2.zero, Vector2.one);
        var ringImg = ringGO.AddComponent<Image>();
        ringImg.sprite = CreateRingSprite(128, 10f);
        ringImg.color  = new Color(1f, 1f, 1f, 0.90f);

        // "E" 텍스트
        var eTxtGO = new GameObject("EKey");
        eTxtGO.transform.SetParent(go.transform, false);
        SetRect(eTxtGO, Vector2.zero, Vector2.one);
        var eTxt = eTxtGO.AddComponent<Text>();
        eTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        eTxt.fontSize  = 80;
        eTxt.fontStyle = FontStyle.Bold;
        eTxt.color     = new Color(1f, 0.90f, 0.20f, 1f);
        eTxt.alignment = TextAnchor.MiddleCenter;
        eTxt.text      = "F";
    }

    static void SetRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax)
    {
        var r      = go.AddComponent<RectTransform>();
        r.anchorMin = anchorMin;
        r.anchorMax = anchorMax;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    // 채워진 원 스프라이트 (배경용)
    static Sprite CreateFilledCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist  = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float alpha = Mathf.Clamp01((c - dist) / 1.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }

    // 링 스프라이트 (테두리용)
    static Sprite CreateRingSprite(int size, float ringWidth)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c      = (size - 1) * 0.5f;
        float outerR = c;
        float innerR = c - ringWidth;
        const float aa = 1.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float alpha;
            if (dist > outerR || dist < innerR) alpha = 0f;
            else alpha = Mathf.Min(
                Mathf.Clamp01((outerR - dist) / aa),
                Mathf.Clamp01((dist - innerR) / aa));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }
}
