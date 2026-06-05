using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 에스코트 편대 AI.
// 발진 시퀀스: 앞 기체(플레이어 or BOT_E1)의 경로를 launchDelay초 지연 재생.
// 발진 완료 후: 리더 기준 V자 편대 위치로 부드럽게 수렴.
public class EscortAI : MonoBehaviour
{
    public enum EscortSide { Left, Right }

    // 학익진 오프셋 (리더 로컬 좌표, m)
    static readonly Vector3 LeftOffset  = new Vector3(-32f, -5f, -40f);
    static readonly Vector3 RightOffset = new Vector3( 32f, -5f, -40f);

    [SerializeField] EscortSide _side = EscortSide.Left;

    [Header("편대비행 보간")]
    [SerializeField] float _lerpNear = 4f;
    [SerializeField] float _lerpFar  = 18f;
    [SerializeField] float _snapDist = 40f;
    [SerializeField] float _rotLerp  = 5f;

    [Header("순차 발진 (경로 재생)")]
    [Tooltip("앞 기체가 출발한 뒤 대기하는 시간 (초)")]
    [SerializeField] float _launchDelay          = 4f;
    [Tooltip("경로 재생 후 편대비행으로 전환할 때까지의 시간 (초)")]
    [SerializeField] float _formationAfterSeconds = 30f;

    Transform       _leader;
    AIBotController _bot;
    bool            _waitingForLaunch;

    // spawnedOnDeck: 항모 갑판 스폰 → BeginLaunchSequence() 호출 전까지 정지
    // 위치·회전은 호출 전 BuildAIObject에서 이미 설정되어 있으므로 건드리지 않음
    public void Initialize(Transform leader, EscortSide side, bool spawnedOnDeck)
    {
        _leader = leader;
        _side   = side;
        _bot    = GetComponent<AIBotController>();
        _bot.PositionOverride = true;
        _waitingForLaunch = spawnedOnDeck;
    }

    // AIManager.EnableAICombat()에서 호출
    // Bot1: followTarget = player.transform
    // Bot2: followTarget = Bot1.transform  (Bot1이 이미 launchDelay 지연 → Bot2는 2×launchDelay 뒤)
    public void BeginLaunchSequence(Transform followTarget)
    {
        StartCoroutine(LaunchCoroutine(followTarget));
    }

    // ─────────────────────────────────────────────────────────────────────────
    IEnumerator LaunchCoroutine(Transform target)
    {
        const float SampleHz = 20f;
        float       interval = 1f / SampleHz;
        int         bufSize  = Mathf.CeilToInt(_launchDelay * SampleHz);

        var buf = new Queue<(Vector3 pos, Quaternion rot)>(bufSize + 4);

        // ── Phase 1: 갑판 대기 & 타겟 경로 기록 ──────────────────────────
        // _waitingForLaunch = true → Update()는 비활성
        // 버퍼가 launchDelay 분량만큼 차면 발진
        while (buf.Count < bufSize)
        {
            buf.Enqueue((target.position, target.rotation));
            yield return new WaitForSeconds(interval);
        }

        // ── Phase 2: 지나간 경로를 따라 비행 ────────────────────────────
        float   followTimer = 0f;
        Vector3 prevSample  = transform.position;

        while (followTimer < _formationAfterSeconds)
        {
            // 버퍼 최전방(= launchDelay 초 전 위치)을 꺼내고 현재 위치를 뒤에 추가
            var (tPos, tRot) = buf.Dequeue();
            buf.Enqueue((target.position, target.rotation));
            followTimer += interval;

            // 이동 속도를 AIBotController에 알려 브로드캐스트 + 회전 구동
            float speed = Vector3.Distance(tPos, prevSample) / interval;
            prevSample  = tPos;
            _bot.SetTarget(tPos, tRot, speed);

            // interval 동안 위치를 선형 보간 (DOTween DOMove Linear 동등)
            float   elap = 0f;
            Vector3 from = transform.position;

            while (elap < interval)
            {
                elap += Time.deltaTime;
                transform.position = Vector3.Lerp(from, tPos, Mathf.Clamp01(elap / interval));
                yield return null;
            }
            transform.position = tPos;
        }

        // ── Phase 3: 편대비행 전환 ────────────────────────────────────
        _waitingForLaunch = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (_leader == null || _bot == null) return;
        if (_waitingForLaunch) return;

        // ── V자 편대 목표 위치 ────────────────────────────────────────────
        Vector3 localOffset = _side == EscortSide.Left ? LeftOffset : RightOffset;
        Vector3 targetPos   = _leader.TransformPoint(localOffset);

        float dist    = Vector3.Distance(transform.position, targetPos);
        float t       = Mathf.Clamp01(dist / _snapDist);
        float lerpSpd = Mathf.Lerp(_lerpNear, _lerpFar, t);

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * lerpSpd);

        // ── 목표 회전 ─────────────────────────────────────────────────────
        float bankOffset     = _side == EscortSide.Left ? 12f : -12f;
        Quaternion leaderRot = _leader.rotation * Quaternion.Euler(0f, 0f, bankOffset);

        Vector3    toTarget = targetPos - transform.position;
        Quaternion lookRot  = toTarget.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(toTarget.normalized, _leader.up)
            : leaderRot;

        float      blend = Mathf.Clamp01(1f - dist / _snapDist);
        Quaternion rot   = Quaternion.Slerp(lookRot, leaderRot, blend);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * _rotLerp);

        var   leaderPC = _leader.GetComponent<PlayerController>();
        float dispSpd  = leaderPC != null ? leaderPC.CurrentSpeed : 62f;
        _bot.SetTarget(targetPos, rot, dispSpd);
    }
}
