using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public GameObject playerPrefab;

    Dictionary<string, PlayerController> players = new();

    public PlayerController GetPlayer(string nickname)
    {
        players.TryGetValue(nickname, out PlayerController player);
        return player;
    }

    public void CreatePlayer(string nickname, Vector3 pos, Quaternion rot, bool isMove)
    {
        if (players.ContainsKey(nickname))
            return;

        GameObject obj = Instantiate(playerPrefab);
        PlayerController player = obj.GetComponent<PlayerController>();

        player.nickname = nickname;
        bool isLocal = nickname == NetworkManager.Instance.socketClient.myNickname;
        player.isLocalPlayer = isLocal;
        player.transform.position = pos;
        player.transform.rotation = rot;
        player.ClearSnapshots();
        player.AddSnapshot(pos, rot, isMove);

        if (!isLocal)
        {
            PlayerLabel label = obj.AddComponent<PlayerLabel>();
            label.SetNickname(nickname);
        }

        players[nickname] = player;

        Debug.Log($"[Player] Created: {nickname} (local={player.isLocalPlayer})");
    }

    public void MovePlayer(string nickname, Vector3 pos, Quaternion rot, bool isMove, int tick)
    {
        if (!players.TryGetValue(nickname, out PlayerController player))
            return;

        player.AddSnapshot(pos, rot, isMove);
    }

    public void RemovePlayer(string nickname)
    {
        if (!players.TryGetValue(nickname, out PlayerController player))
            return;

        players.Remove(nickname);
        Destroy(player.gameObject);

        Debug.Log($"[Player] Removed: {nickname}");
    }

    public void ClearPlayers()
    {
        foreach (PlayerController player in players.Values)
            Destroy(player.gameObject);

        players.Clear();
    }
}
