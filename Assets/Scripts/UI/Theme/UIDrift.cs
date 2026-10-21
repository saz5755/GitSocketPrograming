using UnityEngine;

/// <summary>
/// 배경용 아주 느린 "숨쉬기" 줌. 메뉴 배경이 정지 화면처럼 보이지 않게 하는 절제된 모션.
/// 늘어나는 사각형(앵커 stretch) 배경 컨테이너에 붙인다. 입력 처리/클릭에는 영향 없음.
/// </summary>
public class UIDrift : MonoBehaviour
{
    [SerializeField] float _zoomRange = 0.035f;   // 최대 확대량 (1.012 → 1.047)
    [SerializeField] float _period    = 36f;      // 줌 왕복 주기(초)

    RectTransform _rt;

    void Awake() { _rt = (RectTransform)transform; }

    void Update()
    {
        if (_rt == null) return;
        float ph = Mathf.PingPong(Time.unscaledTime * 2f / Mathf.Max(1f, _period), 1f);
        float z = 1.012f + _zoomRange * (ph * ph * (3f - 2f * ph));   // smoothstep 왕복
        _rt.localScale = new Vector3(z, z, 1f);
    }
}
