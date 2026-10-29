using System.Collections.Generic;
using UnityEngine;

// 원격 미사일: 서버 브로드캐스트 위치를 스냅샷 보간으로 표시
public class RemoteMissileView : MonoBehaviour
{
    public static readonly List<RemoteMissileView> All = new(16);

    /// <summary>표적 닉네임 — 내가 표적이면 RWR/MAWS 에 거리·방위로 표시된다</summary>
    public string      TargetNick  { get; set; } = "";
    public string      ShooterNick { get; set; } = "";
    public MissileSpec Spec        { get; set; }

    TrailRenderer _trail;
    float         _age;

    void Awake()     => All.Add(this);
    void OnDestroy() => All.Remove(this);

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
        _age += Time.deltaTime;
        if (_trail != null && Spec != null) _trail.emitting = _age < Spec.boostTime;
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

    // ── 연기 트레일 (모터 연소 중에만 방출 — 발사자 쪽 MissileController 와 같은 연출) ──
    public void AttachSmokeTrail() => _trail = MissileController.AddSmokeTrail(gameObject);
}
