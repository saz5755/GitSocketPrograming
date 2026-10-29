using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 비행 교관 NPC — 다가가서 [F] 를 누르면 조종사 가이드(조작법·무장·위협 회피)를 열고,
/// 가이드에서 "실전 훈련" 을 시작하면 FlightCoach 가 비행 중 단계별로 안내한다.
///
/// 모델: 프로젝트 로고 모티브의 홀로그램 원자 (AtomInstructorModel) — 런타임에 절차적으로 생성.
/// 배치: 씬의 FlightInstructor_NPC 에 붙이고, 자식 자리표시(Cube)를 원하는 자리에 두면 그 위치에 모델이 선다.
///       QuestNPC 와는 F 키가 겹치지 않게 interactRange 이상 띄워 둘 것.
/// </summary>
public class FlightInstructorNPC : MonoBehaviour
{
    [Header("상호작용")]
    [SerializeField] float interactRange = 3.0f;
    [Tooltip("이 거리 안에 들어오면 말풍선으로 부른다")]
    [SerializeField] float greetRange    = 9f;

    [Header("대사")]
    [TextArea] [SerializeField] string greetCombat  = "조종사! 처음이라면 [F] — 무장 사용과 미사일 회피 요령을 알려 주지.";
    [TextArea] [SerializeField] string greetAirshow = "조종사! 처음이라면 [F] — 조작법과 편대 비행 요령을 알려 주지.";
    [TextArea] [SerializeField] string talkLine     = "브리핑을 시작하지. 끝나면 '실전 훈련' 으로 직접 해 보게.";

    [Header("모델")]
    [Tooltip("모델을 세울 위치. 비우면 씬에 놓아 둔 자리표시 자식(예: Cube)을 찾아 그 자리에 세우고 자리표시는 숨긴다")]
    [SerializeField] Transform modelAnchor;

    AtomInstructorModel _model;
    Transform _anchor;
    Canvas    _bubble;
    Text      _bubbleText, _promptText;
    Transform _camT;
    bool      _near, _inRange;

    /// <summary>상호작용 기준 위치 (모델 발밑)</summary>
    Vector3 Base => _anchor.position;

    void Awake()
    {
        _anchor = ResolveAnchor();
        _model  = _anchor.gameObject.AddComponent<AtomInstructorModel>();
        BuildBubble();
        if (_anchor.GetComponent<Collider>() == null)
        {
            var cap = _anchor.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 1.2f, 0f); cap.height = 1.6f; cap.radius = 0.45f;
        }
    }

    // 자리표시 오브젝트(Cube 등)가 있으면 그 바닥 위치에 모델 기준점을 만들고 자리표시 렌더러를 끈다.
    // 콜라이더는 남겨 플레이어가 통과하지 않게 한다.
    Transform ResolveAnchor()
    {
        if (modelAnchor != null) return modelAnchor;
        MeshRenderer placeholder = null;
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
            if (mr.transform != transform) { placeholder = mr; break; }
        if (placeholder == null) placeholder = GetComponent<MeshRenderer>();
        if (placeholder == null) return transform;

        var b = placeholder.bounds;
        placeholder.enabled = false;
        var anchor = new GameObject("AtomAnchor").transform;
        anchor.SetParent(transform, false);
        anchor.position = new Vector3(b.center.x, b.min.y, b.center.z);
        return anchor;
    }

    void Update()
    {
        var gm = GameModeManager.Instance;
        var gc = gm != null ? gm.GroundCharacter : null;
        bool onFoot = gc != null && !gm.IsFlying && !gm.IsBoardedInCockpit;
        float dist = onFoot ? Vector3.Distance(gc.position, Base) : float.MaxValue;

        _near    = dist <= greetRange;
        _inRange = dist <= interactRange && !gm.HasBoardingTarget && !PilotGuideUI.IsOpen;

        // 모델 상태: 브리핑 중 강한 맥동 / 접근 시 밝아짐
        _model.CurrentMood = PilotGuideUI.IsOpen && dist <= greetRange ? AtomInstructorModel.Mood.Talking
                           : _near ? AtomInstructorModel.Mood.Attentive : AtomInstructorModel.Mood.Idle;

        // 말풍선 / 프롬프트
        _bubble.enabled = _near && !PilotGuideUI.IsOpen;
        if (_bubble.enabled)
        {
            _bubbleText.text = RoomModeSO.IsAirshow ? greetAirshow : greetCombat;
            _promptText.text = _inRange ? "[F]  비행 교관과 대화" : "";
            FaceCamera(_bubble.transform);
        }

        if (_inRange && Input.GetKeyDown(KeyCode.F) && !QuestNpcCloser(gc, dist))
            Talk();
    }

    // QuestNPC 가 더 가까이 있으면 그쪽(QuestManager)이 F 를 처리하게 양보
    static bool QuestNpcCloser(Transform gc, float myDist)
    {
        var q = QuestManager.Instance?.FocusedNPC;
        return q != null && Vector3.Distance(gc.position, q.transform.position) < myDist;
    }

    void Talk()
    {
        _bubbleText.text = talkLine;
        PilotGuideUI.Open(0);
    }

    void FaceCamera(Transform t)
    {
        if (_camT == null) { var c = Camera.main; if (c != null) _camT = c.transform; }
        if (_camT != null) t.rotation = Quaternion.LookRotation(t.position - _camT.position);
    }

    // ── 말풍선 ────────────────────────────────────────────────────────────
    void BuildBubble()
    {
        var go = new GameObject("InstructorBubble");
        go.transform.SetParent(_anchor, false);
        go.transform.localPosition = new Vector3(0f, 3.0f, 0f);   // 원자 모델(코어 1.75m, 궤도 ±0.8m) 위
        go.transform.localScale = Vector3.one * 0.006f;
        _bubble = go.AddComponent<Canvas>();
        _bubble.renderMode = RenderMode.WorldSpace;
        _bubble.sortingOrder = 16;
        ((RectTransform)go.transform).sizeDelta = new Vector2(520f, 150f);

        var bg = new GameObject("BG", typeof(RectTransform)).GetComponent<RectTransform>();
        bg.SetParent(go.transform, false);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.08f, 0.14f, 0.90f);
        var o = bg.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.27f, 0.84f, 0.95f, 0.8f); o.effectDistance = new Vector2(2f, 2f);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text T(string n, Vector2 min, Vector2 max, int fs, Color c, FontStyle st)
        {
            var rt = new GameObject(n, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(go.transform, false);
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = new Vector2(16f, 6f); rt.offsetMax = new Vector2(-16f, -6f);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fs; t.color = c; t.fontStyle = st; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.raycastTarget = false;
            return t;
        }
        T("Name", new Vector2(0f, 0.72f), Vector2.one, 22, new Color(0.27f, 0.84f, 0.95f), FontStyle.Bold).text = "비행 교관";
        _bubbleText = T("Line",   new Vector2(0f, 0.28f), new Vector2(1f, 0.78f), 22, Color.white, FontStyle.Normal);
        _promptText = T("Prompt", Vector2.zero, new Vector2(1f, 0.30f), 22, new Color(1f, 0.85f, 0.2f), FontStyle.Bold);
        _bubble.enabled = false;
    }
}
