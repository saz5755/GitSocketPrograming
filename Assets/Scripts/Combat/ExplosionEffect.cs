using UnityEngine;

// 폭발 연출 진입점 — 절차적 다층 폭발(ExplosionFX)로 위임한다.
// 예전 Resources/ExplosionEffect 프리팹은 더 이상 쓰지 않는다 (호출부 시그니처 호환을 위해 인자만 유지).
public class ExplosionEffect : MonoBehaviour
{
    /// <param name="scale">1 = 미사일 명중, 1.5 = 기체 추락·충돌</param>
    public static void Spawn(Vector3 worldPos, GameObject prefabOverride = null, float scale = 1f)
        => ExplosionFX.Spawn(worldPos, scale);
}
