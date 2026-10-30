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

    /// <summary>CombatHud 의 무장 카드가 탄수를 대신 표시할 때 true (기존 단독 텍스트 숨김)</summary>
    public static bool HideAmmoText;
    public int Ammo    => _ammo;
    public int MaxAmmo => maxAmmo;

    public void SetHUDVisible(bool v)
    {
        if (_overlayCanvas != null) _overlayCanvas.enabled = v;
    }

    // ── 오디오 ────────────────────────────────────────────────────────────────
    AudioSource _gunSrc;
    FlightCamera _cam;

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
    // 매 프레임 재사용 버퍼 (GC 방지)
    readonly List<PlayerController> _leadEnemies = new(MAX_LEAD);
    readonly List<PlayerController> _staleTracks = new();
    RectTransform _overlayRT;

    // ── 색상 팔레트 ───────────────────────────────────────────────────────────
    static readonly Color HG   = new Color(0.00f, 0.98f, 0.42f, 0.92f);
    static readonly Color HGD  = new Color(0.00f, 0.98f, 0.42f, 0.50f);
    static readonly Color WARN = new Color(1.00f, 0.82f, 0.00f, 0.95f);

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>새 출격 — 기총탄 만재</summary>
    public void Rearm() => _ammo = maxAmmo;

    void Awake()
    {
        _ammo = maxAmmo;

        _gunSrc              = gameObject.AddComponent<AudioSource>();
        _gunSrc.clip         = FlightSounds.GunLoop;
        _gunSrc.spatialBlend = 0f;
        _gunSrc.loop         = true;
        _gunSrc.volume       = 0.5f;
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
            _ammoText.gameObject.SetActive(flying && !HideAmmoText);
            if (flying)
            {
                _ammoText.text  = _ammo > 0 ? $"GUN  {_ammo:D3}" : "GUN  EMPTY";
                _ammoText.color = _ammo <= 50 ? WARN : HGD;
            }
        }

        UpdateLeadIndicators();

        bool firing = SimInput.GunTrigger && _ammo > 0 && flying && RoomModeSO.WeaponsEnabled;

        if (firing && !_gunSrc.isPlaying) _gunSrc.Play();
        else if (!firing && _gunSrc.isPlaying)
        {
            _gunSrc.Stop();
            SfxPlayer.Play2D(FlightSounds.GunTail, 0.5f);   // 포신 감속 + 잔향 꼬리
        }
        if (_cam == null) _cam = FindAnyObjectByType<FlightCamera>();
        if (firing && _cam != null) _cam.AddGunShake();

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
        AircraftGunFX.Ensure(_local.gameObject).Fire();

        var sc = NetworkManager.Instance?.socketClient;
        sc?.SendGunFire(sc.myNickname, muzzle, dir);
    }

    // ── 원격 탄 시각 ─────────────────────────────────────────────────────────
    void HandleRemoteGunFire(GunFirePacket p)
    {
        if (p.shooterNickname == _myNick) return;

        // 발사 기체의 총구 섬광 + 3D 기총음
        foreach (var pc in PlayerController.All)
        {
            if (pc == null || pc.nickname != p.shooterNickname) continue;
            AircraftGunFX.Ensure(pc.gameObject).Fire();
            AircraftEngineAudio.Ensure(pc.gameObject).NotifyGunFire();
            break;
        }

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
        // 서버가 발사자 포함 방 전체에 중계 — 피격체 소유자만 피해 적용, 발사자는 히트 마커
        CombatDamage.Apply(p.targetNickname, GameplayConfigSO.Instance.bulletDamage, p.shooterNickname, CombatDamage.WeaponGun);
        CombatDamage.ReportHit(p.shooterNickname, p.targetNickname, CombatDamage.WeaponGun);
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
        var enemies = _leadEnemies;
        enemies.Clear();
        foreach (var pc in PlayerController.All)
        {
            if (enemies.Count >= MAX_LEAD) break;
            if (pc == null || pc == _local || pc.isLocalPlayer) continue;
            if (!pc.IsFlying) continue;
            if (AIManager.IsEscortBot(pc.nickname) || AIManager.IsTankerBot(pc.nickname)) continue;
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

            // ??= 는 직렬화 필드의 에디터 "가짜 null" 을 null 로 보지 않아 할당이 안 된다 → == null 비교
            if (_overlayRT == null) _overlayRT = _overlayCanvas.GetComponent<RectTransform>();
            float w = _overlayRT.sizeDelta.x, h = _overlayRT.sizeDelta.y;
            _leadRTs[i].anchoredPosition = new Vector2((vp.x - 0.5f) * w, (vp.y - 0.5f) * h);

            // 사거리 색상: 1500m 이내 = 녹색, 이상 = 황색
            bool inRange = dist < 1500f;
            if (_leadImgs[i] != null)
                _leadImgs[i].color = inRange
                    ? new Color(0f, 1f, 0f, 0.90f)
                    : new Color(1f, 0.82f, 0f, 0.75f);
        }

        // 이미 사라진 적 추적 정보 정리
        var toRemove = _staleTracks;
        toRemove.Clear();
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
}
