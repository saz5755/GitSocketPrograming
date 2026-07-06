using UnityEngine;

public class AutoLogin
{
    public static void Execute()
    {
        var nm = NetworkManager.Instance;
        if (nm == null) { Debug.Log("[AutoLogin] NetworkManager not found"); return; }

        var sc = nm.socketClient;
        if (sc == null) { Debug.Log("[AutoLogin] socketClient null"); return; }

        Debug.Log($"[AutoLogin] Connecting to {sc.connectIP}:5000 as 'test'");
        sc.Login("test", "1234");
    }
}
