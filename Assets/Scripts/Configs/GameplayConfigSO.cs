using UnityEngine;

/// <summary>
/// 자주 밸런스 조정하는 핵심 게임플레이 값들을 모은 중앙 Config.
/// Assets/Resources/GameplayConfig.asset 에 배치 → Resources.Load로 전역 접근.
///
/// 각 시스템(PlayerController, CountermeasureSystem, HitEffectSystem 등)이
/// Instance 프로퍼티로 참조. 에셋 없으면 하드코딩 기본값으로 폴백.
///
/// 자주 조정하는 값만 모았음:
///   - 배럴롤 회전속도
///   - 플레어 자동 발사 (간격 / 다발 수)
///   - 코브라·Immelmann 기동 (각속도 / 지속시간)
///   - 데미지 (미사일 / 기총)
///
/// 기체별 상세 설정(최대속도, 연료, 스톨 등)은 AircraftStatsSO,
/// 편대 비행 상세는 EscortBehaviorSO 사용 유지.
/// </summary>
[CreateAssetMenu(menuName = "KF21/Gameplay/Gameplay Config", fileName = "GameplayConfig")]
public class GameplayConfigSO : ScriptableObject
{
    // ── 전역 Instance (Resources 자동 로드 + 폴백) ─────────────────────────
    static GameplayConfigSO _cached;
    public static GameplayConfigSO Instance
    {
        get
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<GameplayConfigSO>("GameplayConfig");
            if (_cached == null)
            {
                if (Application.isPlaying)
                    Debug.LogWarning(
                        "[GameplayConfig] Not found — using built-in defaults. " +
                        "Create via Project 우클릭 → Create → KF21 → Gameplay → Gameplay Config, " +
                        "place at Assets/Resources/GameplayConfig.asset");
                _cached = CreateInstance<GameplayConfigSO>();
            }
            return _cached;
        }
    }

    // ── 배럴롤 (V 홀드) ────────────────────────────────────────────────────
    [Header("배럴롤 (V 홀드)")]
    [Tooltip("V 홀드 중 자동 롤 각속도 (deg/s). 240 = 1.5초/회전, 480 = 0.75초/회전")]
    [Range(60f, 720f)] public float barrelRollRate = 240f;

    // ── 플레어 자동 발사 ────────────────────────────────────────────────────
    [Header("플레어 자동 발사 (Q/E 홀드)")]
    [Tooltip("자동 플레어 발사 간격 (초). 0.2 × 2발 = 초당 10발")]
    [Range(0.01f, 1f)] public float autoFlareInterval = 0.2f;
    [Tooltip("1회당 뿌릴 플레어 개수 (burst)")]
    [Range(1, 10)]     public int   autoFlareBurst    = 2;

    // ── 플레어 / 채프 재고 ─────────────────────────────────────────────────
    [Header("플레어 / 채프 재고")]
    [Tooltip("최대 플레어 보유량 (스폰 시 초기값)")]
    [Range(1, 200)] public int flareStockMax = 30;
    [Tooltip("최대 채프 보유량")]
    [Range(1, 200)] public int chaffStockMax = 30;

    // ── 플레어 / 채프 쿨다운 (수동 X, Z + 자동 발사 공통) ────────────────
    [Header("플레어 / 채프 쿨다운 (초)")]
    [Tooltip("플레어 재발사까지 대기 시간")]
    [Range(0.01f, 2f)] public float flareCooldown = 0.35f;
    [Tooltip("채프 재발사까지 대기 시간")]
    [Range(0.01f, 2f)] public float chaffCooldown = 0.35f;

    // ── 플레어 / 채프 수명 ──────────────────────────────────────────────────
    [Header("플레어 / 채프 수명 (초)")]
    [Tooltip("플레어 발광 지속 시간 (VFX 포함 종료)")]
    [Range(0.5f, 15f)] public float flareLifetime = 4f;
    [Tooltip("채프 지속 시간")]
    [Range(0.5f, 15f)] public float chaffLifetime = 6f;

    // ── 플레어 다발 뿌리기 분산 ────────────────────────────────────────────
    [Header("플레어 뿌리기 분산")]
    [Tooltip("생성 위치 분산 반경 (m). 크면 넓게 흩어져 뿌려짐")]
    [Range(0f, 5f)]  public float flareScatterPos = 2.5f;
    [Tooltip("velocity 랜덤 분산 (m/s)")]
    [Range(0f, 20f)] public float flareScatterVel = 10f;
    [Tooltip("측/상하 방향 kick 범위 (m/s). 커튼 전개 폭")]
    [Range(0f, 15f)] public float flareLateralKick = 6f;

    // ── 코브라 / Immelmann (B 키) ──────────────────────────────────────────
    [Header("코브라 / Immelmann 기동 (B 키)")]
    [Tooltip("Phase 0 피치업 각속도 (deg/s)")]
    [Range(60f, 500f)] public float cobraPitchRate = 225f;
    [Tooltip("Phase 0 지속시간 (초) — 피치업 완료까지")]
    [Range(0.2f, 1f)]  public float cobraPhase0Duration = 0.4f;
    [Tooltip("Phase 1 수직 유지 중 B 재입력 대기 타임아웃 (초)")]
    [Range(0.5f, 5f)]  public float cobraHoldTimeout = 2f;
    [Tooltip("Phase 2 Immelmann flip 피치 각속도 (deg/s)")]
    [Range(60f, 500f)] public float immelmannLoopRate = 180f;
    [Tooltip("Phase 2 Immelmann flip 롤 각속도 (deg/s)")]
    [Range(60f, 720f)] public float immelmannRollRate = 360f;
    [Tooltip("Phase 2 지속시간 (초)")]
    [Range(0.3f, 1.5f)] public float immelmannPhase2Duration = 0.5f;

    // ── 데미지 ──────────────────────────────────────────────────────────────
    [Header("데미지")]
    [Tooltip("미사일 1발당 데미지")]
    [Range(1f, 100f)] public float missileDamage = 40f;
    [Tooltip("기총 1발당 데미지")]
    [Range(0.1f, 20f)] public float bulletDamage = 2f;

    // 에디터에서 값 변경 시 캐시 무효화 (실시간 반영)
    void OnValidate() { _cached = this; }
}
