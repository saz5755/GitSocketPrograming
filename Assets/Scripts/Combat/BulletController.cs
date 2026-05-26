using UnityEngine;

public class BulletController : MonoBehaviour
{
    const float HitRadius    = 4f;    // 판정 반경 (m)
    const float BulletLength = 10f;   // 예광탄 시각 길이 (m)
    const float MaxRange     = 700f;  // 최대 사거리 (m)

    Vector3      _velocity;
    bool         _isLocal;
    float        _lifespan;
    LineRenderer _lr;

    public static void Spawn(Vector3 pos, Vector3 dir, float speed, bool isLocal)
    {
        var go = new GameObject("Bullet");
        go.transform.position = pos;
        var bc = go.AddComponent<BulletController>();
        bc._velocity = dir.normalized * speed;
        bc._isLocal  = isLocal;
        bc._lifespan = MaxRange / speed;
    }

    void Awake()
    {
        _lr = gameObject.AddComponent<LineRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        _lr.material             = mat;
        _lr.startColor           = new Color(1.00f, 0.95f, 0.70f, 1.0f);
        _lr.endColor             = new Color(1.00f, 0.55f, 0.10f, 0.0f);
        _lr.startWidth           = 0.18f;
        _lr.endWidth             = 0.04f;
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
        string myNick = NetworkManager.Instance?.socketClient.myNickname ?? "";

        foreach (var pc in FindObjectsOfType<PlayerController>())
        {
            if (pc.isLocalPlayer) continue;

            // 탄 궤적 선분에서 적 기체까지 최단 거리 판정
            Vector3 closest = ClosestPointOnSegment(prevPos, transform.position, pc.transform.position);
            if (Vector3.Distance(closest, pc.transform.position) < HitRadius)
            {
                NetworkManager.Instance?.socketClient.SendGunHit(myNick, pc.nickname, closest);
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
