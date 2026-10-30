using UnityEngine;

public class BulletController : MonoBehaviour
{
    const float HitRadius    = 4f;    // 판정 반경 (m)
    const float BulletLength = 10f;   // 예광탄 시각 길이 (m)
    const float MaxRange     = 700f;  // 최대 사거리 (m)

    Vector3      _velocity;
    bool         _isLocal;
    float        _lifespan;
    string       _myNick;
    LineRenderer _lr;

    /// <param name="aiShooter">호스트가 AI 대신 쏘는 탄이면 그 봇 닉네임 — 명중 판정을 봇 이름으로 보고하고 AI 끼리는 맞지 않는다</param>
    public static void Spawn(Vector3 pos, Vector3 dir, float speed, bool isLocal, string aiShooter = null)
    {
        var go = new GameObject("Bullet");
        go.transform.position = pos;
        var bc = go.AddComponent<BulletController>();
        bc._velocity = dir.normalized * speed;
        bc._isLocal  = isLocal;
        bc._lifespan = MaxRange / speed;
        if (!string.IsNullOrEmpty(aiShooter)) { bc._myNick = aiShooter; bc._aiShooter = true; bc._isLocal = true; }
    }

    bool _aiShooter;

    void Awake()
    {
        _myNick = NetworkManager.Instance?.socketClient?.myNickname ?? "";
        _lr = gameObject.AddComponent<LineRenderer>();
        // HDR 가산 예광탄 (블룸으로 빛남) — 공유 머티리얼 (예전엔 탄마다 new Material 로 누수)
        _lr.sharedMaterial       = FXLib.Tracer;
        _lr.startColor           = new Color(1.00f, 0.80f, 0.45f, 1.0f);
        _lr.endColor             = new Color(1.00f, 0.35f, 0.08f, 0.0f);
        _lr.startWidth           = 0.32f;
        _lr.endWidth             = 0.08f;
        _lr.positionCount        = 2;
        _lr.useWorldSpace        = true;
        _lr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lr.receiveShadows       = false;
    }

    void Update()
    {
        float   dt      = Time.deltaTime;
        Vector3 prevPos = transform.position;
        transform.position += _velocity * dt;

        // 예광탄 시각 업데이트
        _lr.SetPosition(0, transform.position);
        _lr.SetPosition(1, transform.position - _velocity.normalized * BulletLength);

        // 로컬 플레이어 발사 탄만 충돌 판정
        if (_isLocal) CheckHit(prevPos);

        _lifespan -= dt;
        if (_lifespan <= 0f) Destroy(gameObject);
    }

    void CheckHit(Vector3 prevPos)
    {
        const float hitRadiusSq = HitRadius * HitRadius;

        foreach (var pc in PlayerController.All)
        {
            if (pc == null) continue;
            if (_aiShooter ? (pc.nickname == _myNick || AIManager.IsAIBot(pc.nickname) || !TargetingSystem.IsAirborne(pc)) : pc.isLocalPlayer) continue;

            Vector3 closest = ClosestPointOnSegment(prevPos, transform.position, pc.transform.position);
            if ((closest - pc.transform.position).sqrMagnitude < hitRadiusSq)
            {
                NetworkManager.Instance?.socketClient.SendGunHit(_myNick, pc.nickname, closest);
                HitEffectSystem.Instance?.TriggerBulletHit(closest, hitLocalPlayer: false);
                Destroy(gameObject);
                return;
            }
        }
    }

    static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float   sq = ab.sqrMagnitude;
        if (sq < 1e-6f) return a;
        float t = Vector3.Dot(p - a, ab) / sq;
        return a + Mathf.Clamp01(t) * ab;
    }
}
