using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;
    public SocketClient socketClient;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        // SocketClient lives on the same persistent GameObject
        socketClient = GetComponent<SocketClient>() ?? gameObject.AddComponent<SocketClient>();
    }

    public bool IsConnected() => socketClient != null && socketClient.IsConnected();
}
