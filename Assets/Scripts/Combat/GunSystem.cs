using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// M61A2 Vulcan 기총 시스템
// FlightHUD와 동일 GameObject에 부착, 로컬+원격 탄 처리 모두 담당
public class GunSystem : MonoBehaviour
{
    [Header("Gun Settings")]
    [SerializeField] float fireRate     = 20f;   // 발/초 (게임 스케일)
    [SerializeField] int   maxAmmo      = 500;
    [SerializeField] float bulletSpeed  = 400f;  // m/s
    [SerializeField] float muzzleOffset = 5f;    // 기체 전방 포구 거리 (m)

    // ── 상태 ──────────────────────────────────────────────────────────────────
    PlayerController _local;
    string           _myNick;
    int              _ammo;
    float            _fireTimer;

    public int Ammo    => _ammo;
    public int MaxAmmo => maxAmmo;

    public void SetHUDVisible(bool v)
    {
        if (_overlayCanvas != null) _overlayCanvas.enabled = v;
    }

    // ── 오디오 ────────────────────────────────────────────────────────────────
    AudioSource _gunSrc;

    // ── UI ────────────────────────────────────────────────────────────────────
    [SerializeField] Canvas _overlayCanvas;
    [SerializeField] Text   _ammoText;
    bool _uiBuilt;

    // ── Lead Indicator — 적기 당 1개, 최대 MAX_LEAD ───────────────────────────
    const int MAX_LEAD = 8;
    RectTransform[] _leadRTs  = new RectTransform[MAX_LEAD];
    Image[]         _leadImgs = new Image[MAX_LEAD];

    // 적기별 속도 추정 (지수 평활)
    class EnemyTrack { public Vector3 vel; public Vector3 prevPos; public bool set; }
    readonly Dictionary<PlayerController, EnemyTrack> _tracks = new();

    // ── 색상 팔레트 ───────────────────────────────────────────────────────────
    static readonly Color HG   = new Color(0.00f, 0.98f, 0.42f, 0.92f);
    static readonly Color HGD  = new Color(0.00f, 0.98f, 0.42f, 0.50f);
    static readonly Color WARN = new Color(1.00f, 0.82f, 0.00f, 0.95f);

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _ammo = maxAmmo;

        _gunSrc              = gameObject.AddComponent<AudioSource>();
        _gunSrc.clip         = MakeGunLoop(22050);
        _gunSrc.spatialBlend = 0f;
        _gunSrc.loop         = true;
        _gunSrc.volume       = 0.38f;
        _gunSrc.playOnAwake  = false;
        _gunSrc.priority     = 32;
    }

    void Start()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnGunFire += HandleRemoteGunFire;
            sc.OnGunHit  += HandleGunHit;
        }
    }

    void OnDestroy()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnGunFire -= HandleRemoteGunFire;
            sc.OnGunHit  -= HandleGunHit;
        }
    }

    void Update()
    {
        if (_local == null || !_local.isLocalPlayer)
        {
            _local = null;
            foreach (var pc in PlayerController.All)
                if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
            if (_local == null) return;
            _myNick = NetworkManager.Instance?.socketClient?.myNickname ?? "";
        }

        if (!_uiBuilt) { BuildUI(); _uiBuilt = true; }

        bool flying = GameModeManager.Instance?.IsFlying == true;

        // 탄수 텍스트 — 비행 중에만 표시
        if (_ammoText != null)
        {
            _ammoText.gameObject.SetActive(flying);
            if (flying)
            {
                _ammoText.text  = _ammo > 0 ? $"GUN  {_ammo:D3}" : "GUN  EMPTY";
                _ammoText.color = _ammo <= 50 ? WARN : HGD;
            }
        }

        UpdateLeadIndicators();

        bool firing = Input.GetMouseButton(0) && _ammo > 0 && flying;

        if (firing && !_gunSrc.isPlaying) _gunSrc.Play();
        else if (!firing && _gunSrc.isPlaying) _gunSrc.Stop();

        if (!firing) { _fireTimer = 0f; return; }

        float interval = 1f / fireRate;
        _fireTimer += Time.deltaTime;
        while (_fireTimer >= interval && _ammo > 0)
        {
            _fireTimer -= interval;
            Fire();
        }
    }

    // ── 발사 ──────────────────────────────────────────────────────────────────
    void Fire()
    {
        _ammo--;
        Vector3 muzzle = _local.transform.position + _local.transform.forward * muzzleOffset;
        Vector3 dir    = _local.transform.forward;

        BulletController.Spawn(muzzle, dir, bulletSpeed, isLocal: true);

        var sc = NetworkManager.Instance?.socketClient;
        sc?.SendGunFire(sc.myNickname, muzzle, dir);
    }

    // ── 원격 탄 시각 ─────────────────────────────────────────────────────────
    void HandleRemoteGunFire(GunFirePacket p)
    {
        if (p.shooterNickname == _myNick) return;

        BulletController.Spawn(
            new Vector3(p.posX, p.posY, p.posZ),
            new Vector3(p.dirX, p.dirY, p.dirZ).normalized,
            bulletSpeed, isLocal: false);
    }

    // ── 피격 처리 ────────────────────────────────────────────────────────────
    void HandleGunHit(GunHitPacket p)
    {
        var pos = new Vector3(p.posX, p.posY, p.posZ);
        bool hitMe = p.targetNickname == _myNick;
        HitEffectSystem.Instance?.TriggerBulletHit(pos, hitMe);
    }

    // ── 예측 조준 (Lead Indicator) — 모든 적기 표시 ───────────────────────────
    void UpdateLeadIndicators()
    {
        // 비행 중이 아니면 전부 숨김
        if (GameModeManager.Instance?.IsFlying != true)
        {
            for (int i = 0; i < MAX_LEAD; i++)
                if (_leadRTs[i] != null) _leadRTs[i].gameObject.SetActive(false);
            _tracks.Clear();
            return;
        }

        Camera cam = Camera.main;

        // 현재 프레임에 표시할 적기 목록 수집 (에스코트 봇 제외)
        var enemies = new List<PlayerController>(MAX_LEAD);
        foreach (var pc in PlayerController.All)
        {
            if (enemies.Count >= MAX_LEAD) break;
            if (pc == null || pc == _local || pc.isLocalPlayer) continue;
            if (!pc.IsFlying) continue;
            if (AIManager.IsEscortBot(pc.nickname)) continue;
            enemies.Add(pc);
        }

        // 슬롯별 업데이트
        for (int i = 0; i < MAX_LEAD; i++)
        {
            if (_leadRTs[i] == null) continue;

            if (i >= enemies.Count || cam == null)
            {
                _leadRTs[i].gameObject.SetActive(false);
                continue;
            }

            PlayerController enemy = enemies[i];

            // 속도 추정 (지수 평활)
            if (!_tracks.TryGetValue(enemy, out var track))
            {
                track = new EnemyTrack();
                _tracks[enemy] = track;
            }

            if (track.set)
            {
                Vector3 raw = (enemy.transform.position - track.prevPos) / Mathf.Max(Time.deltaTime, 0.001f);
                track.vel = Vector3.Lerp(track.vel, raw, Time.deltaTime * 6f);
            }
            track.prevPos = enemy.transform.position;
            track.set     = true;

            // 예측 위치 계산
            float   dist    = Vector3.Distance(_local.transform.position, enemy.transform.position);
            float   tof     = dist / bulletSpeed;
            Vector3 leadPos = enemy.transform.position + track.vel * tof;

            Vector3 vp = cam.WorldToViewportPoint(leadPos);
            if (vp.z < 0f)
            {
                _leadRTs[i].gameObject.SetActive(false);
                continue;
            }

            _leadRTs[i].gameObject.SetActive(true);

            var canvasRT = _overlayCanvas.GetComponent<RectTransform>();
            float w = canvasRT.sizeDelta.x, h = canvasRT.sizeDelta.y;
            _leadRTs[i].anchoredPosition = new Vector2((vp.x - 0.5f) * w, (vp.y - 0.5f) * h);

            // 사거리 색상: 1500m 이내 = 녹색, 이상 = 황색
            bool inRange = dist < 1500f;
            if (_leadImgs[i] != null)
                _leadImgs[i].color = inRange
                    ? new Color(0f, 1f, 0f, 0.90f)
                    : new Color(1f, 0.82f, 0f, 0.75f);
        }

        // 이미 사라진 적 추적 정보 정리
        var toRemove = new List<PlayerController>();
        foreach (var kv in _tracks)
            if (kv.Key == null || !PlayerController.All.Contains(kv.Key))
                toRemove.Add(kv.Key);
        foreach (var k in toRemove) _tracks.Remove(k);
    }

    // ── UI 생성 ───────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        if (_overlayCanvas == null)
        {
            var hud = GetComponent<FlightHUD>();
            var go = new GameObject("GunOverlay_Canvas");
            if (hud != null && hud.HudRoot != null)
                go.transform.SetParent(hud.HudRoot, false);
            _overlayCanvas = go.AddComponent<Canvas>();
            _overlayCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 22;
            var cs = go.AddComponent<CanvasScaler>();
            cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.matchWidthOrHeight  = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }
        else
        {
            var hud = GetComponent<FlightHUD>();
            if (hud != null && hud.HudRoot != null)
                _overlayCanvas.transform.SetParent(hud.HudRoot, false);
        }

        // 탄수 표시
        if (_ammoText == null)
        {
            var ammoGO = new GameObject("GunAmmoText");
            ammoGO.transform.SetParent(_overlayCanvas.transform, false);
            var ammoRT = ammoGO.AddComponent<RectTransform>();
            ammoRT.anchorMin = ammoRT.anchorMax = new Vector2(0.5f, 0.5f);
            ammoRT.anchoredPosition = new Vector2(636f, -296f);
            ammoRT.sizeDelta        = new Vector2(170f, 28f);
            _ammoText               = ammoGO.AddComponent<Text>();
            _ammoText.text          = $"GUN  {_ammo:D3}";
            _ammoText.font          = font;
            _ammoText.fontSize      = 14;
            _ammoText.color         = HGD;
            _ammoText.alignment     = TextAnchor.MiddleLeft;
            _ammoText.raycastTarget = false;
        }

        // Lead Indicator — MAX_LEAD 개 사전 생성
        for (int i = 0; i < MAX_LEAD; i++)
        {
            if (_leadRTs[i] != null) continue;

            var leadGO = new GameObject($"LeadIndicator_{i}");
            leadGO.transform.SetParent(_overlayCanvas.transform, false);
            _leadRTs[i] = leadGO.AddComponent<RectTransform>();
            _leadRTs[i].anchorMin = _leadRTs[i].anchorMax = new Vector2(0.5f, 0.5f);
            _leadRTs[i].sizeDelta = new Vector2(44f, 44f);
            _leadRTs[i].anchoredPosition = Vector2.zero;
            _leadRTs[i].gameObject.SetActive(false);

            _leadImgs[i]               = leadGO.AddComponent<Image>();
            _leadImgs[i].sprite        = MakeRingSprite(64);
            _leadImgs[i].color         = new Color(0f, 1f, 0f, 0.90f);
            _leadImgs[i].raycastTarget = false;

            // 중심 점
            MkImg(leadGO.transform, Vector2.zero,         new Vector2(5f,  5f),  new Color(0f, 1f, 0f, 1.00f));
            // 십자 팔
            MkImg(leadGO.transform, new Vector2(-27f, 0f), new Vector2(10f, 2f), new Color(0f, 1f, 0f, 0.70f));
            MkImg(leadGO.transform, new Vector2( 27f, 0f), new Vector2(10f, 2f), new Color(0f, 1f, 0f, 0.70f));
            MkImg(leadGO.transform, new Vector2(0f,  27f), new Vector2(2f, 10f), new Color(0f, 1f, 0f, 0.70f));
            MkImg(leadGO.transform, new Vector2(0f, -27f), new Vector2(2f, 10f), new Color(0f, 1f, 0f, 0.70f));
        }
    }

    static void MkImg(Transform parent, Vector2 pos, Vector2 size, Color col)
    {
        var go = new GameObject("el");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var img = go.AddComponent<Image>();
        img.color         = col;
        img.raycastTarget = false;
    }

    // ── 스프라이트 생성 ───────────────────────────────────────────────────────
    static Sprite MakeRingSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float cx = size * 0.5f, cy = size * 0.5f;
        float r = size * 0.5f - 1f, thick = r * 0.18f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - cx + .5f) * (x - cx + .5f) + (y - cy + .5f) * (y - cy + .5f));
                float a = Mathf.Clamp01(Mathf.Min((d - (r - thick)) / 1.5f, (r - d) / 1.5f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px); tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // ── PCM: 개틀링 기총음 루프 (20Hz 펄스 트레인) ────────────────────────────
    static AudioClip MakeGunLoop(int sr)
    {
        int period = sr / 20;
        int n      = period * 4;
        var buf    = new float[n];
        var rng    = new System.Random(77);
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / sr;
            float ph  = (float)(i % period) / period;
            float env = Mathf.Clamp01(ph / 0.008f) * Mathf.Exp(-ph * 38f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float body  = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.38f
                        + Mathf.Sin(2f * Mathf.PI * 390f * t) * 0.22f;
            buf[i] = (noise * 0.48f + body * 0.52f) * env * 0.80f;
        }
        int fade = sr / 200;
        for (int i = 0; i < fade && i < n; i++)
        {
            float f = (float)i / fade;
            buf[i]         *= f;
            buf[n - 1 - i] *= f;
        }
        var clip = AudioClip.Create("GunLoop", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }
}
