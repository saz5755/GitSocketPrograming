using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Text;

public class GroundJitterProbe
{
    // GroundController에 붙어 자동 이동 후 위치 변화를 측정하는 MonoBehaviour
    class JitterMonitor : MonoBehaviour
    {
        GroundController _gc;
        CharacterController _cc;
        int   _frame;
        float _timer;
        Vector3 _prevPos;
        float _maxJitter;
        float _maxYJitter;
        StringBuilder _log = new StringBuilder();

        void Start()
        {
            _gc = GetComponent<GroundController>();
            _cc = GetComponent<CharacterController>();
            _prevPos = transform.position;
            _log.AppendLine("[JitterProbe] 측정 시작 (3초 이동 후 보고)");
            Debug.Log("[JitterProbe] 측정 시작");
        }

        void Update()
        {
            _timer += Time.deltaTime;
            _frame++;

            Vector3 pos = transform.position;
            float   dy  = Mathf.Abs(pos.y - _prevPos.y);
            float   dxz = new Vector2(pos.x - _prevPos.x, pos.z - _prevPos.z).magnitude;
            float   dt  = Mathf.Sqrt(dy * dy + dxz * dxz);

            if (_timer > 0.5f) // 초기 스폰 이동 무시
            {
                if (dy  > _maxYJitter)  _maxYJitter  = dy;
                if (dt  > _maxJitter)   _maxJitter   = dt;
            }

            // 수직 덜덜거림 감지 (2프레임 연속 반대 방향 이동)
            if (_timer > 0.5f && dy > 0.002f)
                _log.AppendLine($"  frame={_frame} t={_timer:F2}s  dy={dy:F4}m  dxz={dxz:F4}m  animSpeed={_gc?.CurrentAnimSpeed:F3}  animState={_gc?.CurrentAnimState}  grounded={_cc?.isGrounded}");

            _prevPos = pos;

            if (_timer >= 4f)
            {
                Debug.Log($"[JitterProbe] 결과: maxJitter={_maxJitter*1000:F2}mm  maxY={_maxYJitter*1000:F2}mm  frames={_frame}");
                if (_log.Length > 100)
                    Debug.Log(_log.ToString());
                else
                    Debug.Log("[JitterProbe] 수직 덜덜거림 없음 — 정상");
                Destroy(this);
            }
        }
    }

    public static void Execute()
    {
        var gc = Object.FindFirstObjectByType<GroundController>();
        if (gc == null) { Debug.Log("[JitterProbe] GroundController not found — 플레이어 스폰 후 실행"); return; }

        if (gc.GetComponent<JitterMonitor>() != null)
        { Debug.Log("[JitterProbe] 이미 실행 중"); return; }

        gc.gameObject.AddComponent<JitterMonitor>();
        Debug.Log($"[JitterProbe] 모니터 부착: {gc.gameObject.name}  pos={gc.transform.position}");
    }
}
