using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public GameObject playerPrefab;

    Dictionary<string, PlayerController> players = new();

    public PlayerController GetPlayer(string nickname)
    {
        if(players.ContainsKey(nickname))
        {
            return players[nickname];
        }
        
        return null;
    }
    
    public void MovePlayer(string nickname, Vector3 pos, float rotY, bool isMove, int tick)
    {
        Debug.Log($"[MOVE] {nickname} {pos}");
        
        if (!players.ContainsKey(nickname))
            return;

        PlayerController player = players[nickname];

        player.AddSnapshot(pos, rotY, isMove);
        
    }

    public void CreatePlayer(string nickname, Vector3 pos, float rotY, bool isMove)
    {
        Debug.Log($"CreatePlayer 호출 : {nickname}");
     
        
        // 이미 존재하면 생성 안 함
        if(players.ContainsKey(nickname))
            return;

        GameObject obj =
            Instantiate(playerPrefab);

        PlayerController player =
            obj.GetComponent<PlayerController>();
        
        player.ClearSnapshots();
        player.AddSnapshot(pos, rotY, isMove);
        
        player.nickname = nickname;

        player.isLocalPlayer =
        (
            nickname ==
            NetworkManager.Instance
                .socketClient
                .myNickname
        );

        // 최초 위치 설정
        player.transform.position = pos;

        // Remote 보간 초기값 동기화
        player.SetTargetPosition(pos, rotY, isMove);

        players[nickname] = player;

        Debug.Log($"Create Player : {nickname}");
    }
    
    public void RemovePlayer(string nickname)
    {
        if(players.ContainsKey(nickname) == false)
            return;

        PlayerController player = players[nickname];

        players.Remove(nickname);

        Destroy(player.gameObject);

        Debug.Log($"REMOVE PLAYER : {nickname}");
    }
    
    // ??
    public void ClearPlayers()
    {
        foreach(var player in players.Values)
        {
            Destroy(player.gameObject);
        }

        players.Clear();
    }
}