using UnityEngine;

public class ExplosionEffect : MonoBehaviour
{
    const float TOTAL_LIFE = 3.0f;

    public static void Spawn(Vector3 worldPos)
    {
        GameObject prefab = Resources.Load<GameObject>("ExplosionEffect");
        if (prefab != null)
        {
            GameObject go = Instantiate(prefab, worldPos, Quaternion.identity);
            Destroy(go, TOTAL_LIFE);
        }
        else
        {
            Debug.LogError("ExplosionEffect prefab not found in Resources!");
        }
    }
}
