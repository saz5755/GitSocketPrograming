using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 배치되거나 서버를 통해 스폰된 공유 항공기에 부착.
/// networkId로 전체 클라이언트에서 동일한 오브젝트를 식별한다.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class NetworkAircraft : MonoBehaviour
{
    public static readonly Dictionary<int, NetworkAircraft> Registry = new();

    public int networkId;

    public PlayerController PC { get; private set; }

    bool _registered;

    void Awake() => PC = GetComponent<PlayerController>();

    void Start()
    {
        if (!_registered) Register();
    }

    /// <summary>동적 생성 시 networkId를 설정하고 레지스트리에 등록. Start()보다 먼저 호출할 것.</summary>
    public void SetNetworkId(int id)
    {
        if (_registered && Registry.TryGetValue(networkId, out var cur) && cur == this)
            Registry.Remove(networkId);
        networkId = id;
        Register();
    }

    void Register()
    {
        Registry[networkId] = this;
        _registered = true;
    }

    void OnDestroy()
    {
        if (_registered && Registry.TryGetValue(networkId, out var cur) && cur == this)
            Registry.Remove(networkId);
    }

    public static NetworkAircraft GetById(int id)
    {
        Registry.TryGetValue(id, out var a);
        return a;
    }
}
