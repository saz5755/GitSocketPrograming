// 서버 사이드 이동 시뮬레이션 (relay 방식에서는 직접 사용 안 하지만 보존)
using System.Numerics;

static class MovementSim
{
    public static Vector3 Simulate(Vector3 pos, Vector3 dir, float speed, float tickDelta)
    {
        if (dir.Length() > 1f)
            dir = Vector3.Normalize(dir);

        return pos + dir * speed * tickDelta;
    }
}
