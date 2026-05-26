using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 원격 미사일: 서버 브로드캐스트 위치를 스냅샷 보간으로 표시
public class RemoteMissileView : MonoBehaviour
{
    struct PosSnapshot
    {
        public Vector3    pos;
        public Quaternion rot;
        public float      time;
    }

    const float InterpDelay = 0.1f;
    const int   MaxSnaps    = 12;

    readonly List<PosSnapshot> _snaps = new();

    public void AddSnapshot(Vector3 pos, Quaternion rot)
    {
        _snaps.Add(new PosSnapshot { pos = pos, rot = rot, time = Time.time });
        if (_snaps.Count > MaxSnaps) _snaps.RemoveAt(0);
    }

    void Update()
    {
        if (_snaps.Count == 0) return;

        if (_snaps.Count == 1)
        {
            transform.position = _snaps[0].pos;
            transform.rotation = _snaps[0].rot;
            return;
        }

        float renderTime = Time.time - InterpDelay;

        while (_snaps.Count > 2 && _snaps[1].time <= renderTime)
            _snaps.RemoveAt(0);

        var from = _snaps[0];
        var to   = _snaps[1];

        float t = (to.time > from.time)
            ? Mathf.Clamp01(Mathf.InverseLerp(from.time, to.time, renderTime))
            : 1f;

        transform.position = Vector3.Lerp(from.pos, to.pos, t);
        transform.rotation = Quaternion.Slerp(from.rot, to.rot, t);
    }

    // ── 연기 트레일 추가 (MissileController와 동일한 셰이더) ─────────────
    public void AttachSmokeTrail()
    {
        var tr = gameObject.AddComponent<TrailRenderer>();
        tr.time              = 1.8f;
        tr.startWidth        = 0.5f;
        tr.endWidth          = 0.05f;
        tr.minVertexDistance = 0.3f;
        tr.textureMode       = LineTextureMode.Stretch;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows    = false;

        var sh = Shader.Find("Custom/SmokeTrail");
        Material mat;
        if (sh != null)
        {
            mat = new Material(sh);
            mat.SetColor("_Color",      new Color(0.75f, 0.72f, 0.70f, 0.55f));
            mat.SetFloat("_NoiseScale", 3.5f);
        }
        else
        {
            var fallback = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                        ?? Shader.Find("Sprites/Default");
            mat = new Material(fallback);
            mat.color = new Color(0.7f, 0.7f, 0.7f, 0.45f);
        }
        tr.material = mat;

        var gc = new Gradient();
        gc.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.90f, 0.70f), 0.00f),
                new GradientColorKey(new Color(0.65f, 0.62f, 0.60f), 0.30f),
                new GradientColorKey(new Color(0.30f, 0.28f, 0.27f), 1.00f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.80f, 0.00f),
                new GradientAlphaKey(0.45f, 0.40f),
                new GradientAlphaKey(0.00f, 1.00f),
            });
        tr.colorGradient = gc;
    }
}
