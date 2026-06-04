using UnityEngine;

/// <summary>
/// READY 레이블이 항상 카메라 정면을 향하도록 회전.
/// </summary>
public class ReadyLabelFaceCamera : MonoBehaviour
{
    Transform _cam;

    void Start()  => _cam = Camera.main?.transform;

    void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main?.transform;
        if (_cam != null) transform.forward = _cam.forward;
    }
}
