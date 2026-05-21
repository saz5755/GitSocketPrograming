using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;

    public SocketClient socketClient;

    void Awake()
    {
        Instance = this;
        socketClient = FindObjectOfType<SocketClient>();
    }

    public bool IsConnected()
    {
        return socketClient != null && socketClient.IsConnected();
    }
}
