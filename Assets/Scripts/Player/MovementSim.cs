using UnityEngine;

public static class MovementSim
{
    public static Vector3 Simulate(Vector3 pos, Vector3 dir, float speed, float tickDelta)
    {
        if (dir.magnitude > 1f)
            dir = dir.normalized;

        return pos + dir * speed * tickDelta;
    }
}
