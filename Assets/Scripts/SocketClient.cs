using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class SocketClient : MonoBehaviour
{
    TcpClient    socket;
    UdpClient    udp;
    NetworkStream stream;
    Thread       receiveThread;
    Thread       udpReceiveThread;
    readonly object sendLock = new();

    public string myNickname;
    volatile bool isRunning;

    [Header("Connection Defaults")]
    public string connectIP = "172.30.201.117";
    public int    tcpPort   = 5000;
    public int    udpPort   = 6000;

    // UDP 틱 추적 (스레드 안전, PlayerManager 불필요)
    readonly ConcurrentDictionary<string, int> lastTicks = new();

    // ── 이벤트 (Unity 메인스레드에서 발행) ──────────────────────────────
    public event Action<LoginResultPacket>    OnLoginResult;
    public event Action<string>               OnChat;
    public event Action<SpawnPacket>          OnSpawn;
    public event Action<DespawnPacket>        OnDespawn;
    public event Action<MoveBroadcastPacket>  OnMove;
    public event Action<MoveAckPacket>        OnMoveAck;
    public event Action<List<RoomInfo>>       OnRoomList;
    public event Action<MissileSpawnPacket>   OnMissileSpawn;
    public event Action<MissileDestroyPacket> OnMissileDestroy;
    public event Action<MissileWarnPacket>    OnMissileWarn;
    public event Action<MissileMovePacket>    OnMissileMove;
    public event Action<GunFirePacket>          OnGunFire;
    public event Action<GunHitPacket>           OnGunHit;
    public event Action<CreateRoomResultPacket> OnCreateRoomResult;
    public event Action<EnterRoomResultPacket>  OnEnterRoomResult;
    public event Action<QuestUpdatePacket>      OnQuestUpdate;
    public event Action<AISpawnPacket>          OnAISpawn;
    public event Action<string>                 OnAIDespawn;
    public event Action<AIMovePacket>           OnAIMove;
    public event Action<HostChangePacket>       OnHostChange;
    public event Action<AircraftBoardPacket>    OnAircraftBoard;
    public event Action<AircraftBoardPacket>    OnAircraftLeave;

    // ── 연결 ──────────────────────────────────────────────────────────────
    public void Connect(string ip = null)
    {
        if (IsConnected()) return;

        string host = ip
            ?? (GameManager.Instance != null ? GameManager.Instance.serverIP : null)
            ?? connectIP;
        connectIP = host;

        socket   = new TcpClient(host, tcpPort);
        stream   = socket.GetStream();
        isRunning = true;

        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();

        udp = new UdpClient(0);
        udp.Connect(host, udpPort);

        udpReceiveThread = new Thread(UdpReceiveLoop) { IsBackground = true };
        udpReceiveThread.Start();

        Debug.Log($"[Net] Connected to {host}:{tcpPort}");
    }

    // ── TCP 수신 루프 ──────────────────────────────────────────────────────
    void ReceiveLoop()
    {
        byte[] lenBuf = new byte[4];
        while (isRunning)
        {
            try
            {
                ReadFull(stream, lenBuf, 4);
                int length = BitConverter.ToInt32(lenBuf, 0);
                byte[] dataBuf = new byte[length];
                ReadFull(stream, dataBuf, length);
                string json = Encoding.UTF8.GetString(dataBuf);
                JObject obj  = JObject.Parse(json);
                PacketType t = (PacketType)obj["type"].Value<int>();
                HandleTCP(t, json);
            }
            catch (IOException)  { Debug.Log("[TCP] Disconnected"); break; }
            catch (SocketException) { Debug.Log("[TCP] Socket closed"); break; }
            catch (Exception e)  { if (isRunning) Debug.LogError($"[TCP] {e}"); }
        }
    }

    void HandleTCP(PacketType type, string json)
    {
        switch (type)
        {
            case PacketType.LOGIN_RESULT:
            {
                var p = JsonConvert.DeserializeObject<LoginResultPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    if (p.success) StartCoroutine(RegisterUDP());
                    OnLoginResult?.Invoke(p);
                });
                break;
            }

            case PacketType.CHAT:
            case PacketType.SYSTEM:
            {
                var p   = JsonConvert.DeserializeObject<ChatPacket>(json);
                string msg = p.type == PacketType.CHAT
                    ? $"[{p.nickname}] {p.message}"
                    : $"<color=#FFD700>◈ {p.message}</color>";
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnChat?.Invoke(msg));
                break;
            }

            case PacketType.SPAWN:
            {
                var p = JsonConvert.DeserializeObject<SpawnPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnSpawn?.Invoke(p));
                break;
            }

            case PacketType.DESPAWN:
            {
                var p = JsonConvert.DeserializeObject<DespawnPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    lastTicks.TryRemove(p.nickname, out _);
                    OnDespawn?.Invoke(p);
                });
                break;
            }

            case PacketType.MOVE:
            {
                var p = JsonConvert.DeserializeObject<MoveBroadcastPacket>(json);
                if (p.nickname == myNickname) break;
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnMove?.Invoke(p));
                break;
            }

            case PacketType.ROOM_LIST_RESULT:
            {
                var p = JsonConvert.DeserializeObject<RoomListResultPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnRoomList?.Invoke(p.rooms));
                break;
            }

            case PacketType.MISSILE_SPAWN:
            {
                var p = JsonConvert.DeserializeObject<MissileSpawnPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnMissileSpawn?.Invoke(p));
                break;
            }

            case PacketType.MISSILE_DESTROY:
            {
                var p = JsonConvert.DeserializeObject<MissileDestroyPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnMissileDestroy?.Invoke(p));
                break;
            }

            case PacketType.MISSILE_WARN:
            {
                var p = JsonConvert.DeserializeObject<MissileWarnPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnMissileWarn?.Invoke(p));
                break;
            }

            case PacketType.GUN_HIT:
            {
                var p = JsonConvert.DeserializeObject<GunHitPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnGunHit?.Invoke(p));
                break;
            }

            case PacketType.CREATE_ROOM_RESULT:
            {
                var p = JsonConvert.DeserializeObject<CreateRoomResultPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnCreateRoomResult?.Invoke(p));
                break;
            }

            case PacketType.ENTER_ROOM_RESULT:
            {
                var p = JsonConvert.DeserializeObject<EnterRoomResultPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnEnterRoomResult?.Invoke(p));
                break;
            }

            case PacketType.QUEST_UPDATE:
            {
                var p = JsonConvert.DeserializeObject<QuestUpdatePacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnQuestUpdate?.Invoke(p));
                break;
            }

            case PacketType.AI_SPAWN:
            {
                var p = JsonConvert.DeserializeObject<AISpawnPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnAISpawn?.Invoke(p));
                break;
            }

            case PacketType.AI_DESPAWN:
            {
                var p = JsonConvert.DeserializeObject<DespawnPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    lastTicks.TryRemove(p.nickname, out _);
                    OnAIDespawn?.Invoke(p.nickname);
                });
                break;
            }

            case PacketType.HOST_CHANGE:
            {
                var p = JsonConvert.DeserializeObject<HostChangePacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnHostChange?.Invoke(p));
                break;
            }

            case PacketType.BOARD_AIRCRAFT:
            {
                var p = JsonConvert.DeserializeObject<AircraftBoardPacket>(json);
                if (p.nickname != myNickname)
                    UnityMainThreadDispatcher.Instance.Enqueue(() => OnAircraftBoard?.Invoke(p));
                break;
            }

            case PacketType.LEAVE_AIRCRAFT:
            {
                var p = JsonConvert.DeserializeObject<AircraftBoardPacket>(json);
                if (p.nickname != myNickname)
                    UnityMainThreadDispatcher.Instance.Enqueue(() => OnAircraftLeave?.Invoke(p));
                break;
            }

            default: break;
        }
    }

    // ── UDP: 등록 + keepalive ──────────────────────────────────────────────
    bool keepAliveRunning;

    IEnumerator RegisterUDP()
    {
        SendUdpConnect();
        yield return new WaitForSeconds(0.1f);
        SendUdpConnect();
        yield return new WaitForSeconds(0.2f);
        if (!keepAliveRunning)
        {
            keepAliveRunning = true;
            StartCoroutine(KeepUdpAlive());
        }
    }

    IEnumerator KeepUdpAlive()
    {
        while (IsConnected())
        {
            yield return new WaitForSeconds(5f);
            if (IsConnected()) SendUdpConnect();
        }
        keepAliveRunning = false;
    }

    // ── UDP 수신 루프 ──────────────────────────────────────────────────────
    void UdpReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        while (isRunning)
        {
            try
            {
                byte[] data = udp.Receive(ref ep);
                string json = Encoding.UTF8.GetString(data);
                JObject obj = JObject.Parse(json);
                var type = (PacketType)obj["type"].Value<int>();

                switch (type)
                {
                    case PacketType.MOVE:
                    {
                        var p = JsonConvert.DeserializeObject<MoveBroadcastPacket>(json);
                        if (p.nickname == myNickname) break;

                        // 오래된 패킷 드롭
                        if (p.tick > 0)
                        {
                            int last = lastTicks.GetOrAdd(p.nickname, 0);
                            if (p.tick <= last) break;
                            lastTicks[p.nickname] = p.tick;
                        }
                        UnityMainThreadDispatcher.Instance.Enqueue(() => OnMove?.Invoke(p));
                        break;
                    }

                    case PacketType.MOVE_ACK:
                    {
                        var p = JsonConvert.DeserializeObject<MoveAckPacket>(json);
                        UnityMainThreadDispatcher.Instance.Enqueue(() => OnMoveAck?.Invoke(p));
                        break;
                    }

                    case PacketType.MISSILE_MOVE:
                    {
                        var p = JsonConvert.DeserializeObject<MissileMovePacket>(json);
                        UnityMainThreadDispatcher.Instance.Enqueue(() => OnMissileMove?.Invoke(p));
                        break;
                    }

                    case PacketType.GUN_FIRE:
                    {
                        var p = JsonConvert.DeserializeObject<GunFirePacket>(json);
                        UnityMainThreadDispatcher.Instance.Enqueue(() => OnGunFire?.Invoke(p));
                        break;
                    }

                    case PacketType.AI_MOVE:
                    {
                        var p = JsonConvert.DeserializeObject<AIMovePacket>(json);
                        UnityMainThreadDispatcher.Instance.Enqueue(() => OnAIMove?.Invoke(p));
                        break;
                    }
                }
            }
            catch (SocketException se)
                when (se.SocketErrorCode == SocketError.Interrupted ||
                      se.SocketErrorCode == SocketError.OperationAborted)
            {
                // udp.Close()로 인한 정상 종료 — 로그 불필요
                break;
            }
            catch (ObjectDisposedException)
            {
                // 소켓 Dispose 이후 접근 — 정상 종료
                break;
            }
            catch (Exception e)
            {
                if (isRunning) Debug.LogError($"[UDP] {e.Message}");
                break;
            }
        }
    }

    // ── 송신 ──────────────────────────────────────────────────────────────
    public void Login(string id, string pw)
    {
        if (!IsConnected()) Connect();
        myNickname = id;
        if (GameManager.Instance != null) GameManager.Instance.myNickname = id;
        SendTCP(new LoginPacket { type = PacketType.LOGIN, id = id, password = pw });
    }

    public void SendChat(string msg)
        => SendTCP(new ChatPacket { type = PacketType.CHAT, message = msg });

    public void EnterRoom(int roomId)
    {
        StartCoroutine(RegisterUDP());
        SendTCP(new EnterRoomPacket { type = PacketType.ENTER_ROOM, roomId = roomId });
    }

    public void LeaveRoom()
    {
        lastTicks.Clear();
        SendTCP(new LeaveRoomPacket { type = PacketType.LEAVE_ROOM });
    }

    public void RequestRoomList()
        => SendTCP(new Packet { type = PacketType.ROOM_LIST_REQUEST });

    public void SendCreateRoom(string roomName, int maxPlayers = 8)
        => SendTCP(new CreateRoomPacket { type = PacketType.CREATE_ROOM, roomName = roomName, maxPlayers = maxPlayers });

    public void SendMove(float posX, float posY, float posZ,
                         float rotX, float rotY, float rotZ,
                         bool isMove, bool isFlying, int tick,
                         bool isBoardedInCockpit = false,
                         int  animState          = 0)
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new MovePacket
        {
            type = PacketType.MOVE, tick = tick,
            posX = posX, posY = posY, posZ = posZ,
            rotX = rotX, rotY = rotY, rotZ = rotZ,
            isMove = isMove, isFlying = isFlying,
            isBoardedInCockpit = isBoardedInCockpit,
            animState          = animState
        }));
        try { udp?.Send(data, data.Length); }
        catch { }
    }

    public void SendMissileSpawn(string id, string shooter, string target,
                                 Vector3 pos, Vector3 rot, int guidanceType)
        => SendTCP(new MissileSpawnPacket {
            type = PacketType.MISSILE_SPAWN, missileId = id,
            shooterNickname = shooter, targetNickname = target,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            rotX = rot.x, rotY = rot.y, rotZ = rot.z,
            guidanceType = guidanceType });

    public void SendMissileDestroy(string id, string shooterNick, string hitNick, Vector3 pos)
        => SendTCP(new MissileDestroyPacket {
            type = PacketType.MISSILE_DESTROY, missileId = id,
            shooterNickname = shooterNick, hitNickname = hitNick,
            posX = pos.x, posY = pos.y, posZ = pos.z });

    public void SendMissileWarn(string shooter, string target, int lockLevel)
        => SendTCP(new MissileWarnPacket {
            type = PacketType.MISSILE_WARN,
            shooterNickname = shooter, targetNickname = target,
            lockLevel = lockLevel });

    public void SendGunFire(string shooterNick, Vector3 muzzlePos, Vector3 dir)
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new GunFirePacket
        {
            type             = PacketType.GUN_FIRE,
            shooterNickname  = shooterNick,
            posX = muzzlePos.x, posY = muzzlePos.y, posZ = muzzlePos.z,
            dirX = dir.x,       dirY = dir.y,       dirZ = dir.z
        }));
        try { udp?.Send(data, data.Length); }
        catch { }
    }

    public void SendQuestUpdate(string questId, string questName, int stepIndex, int totalSteps, bool isComplete)
        => SendTCP(new QuestUpdatePacket {
            type       = PacketType.QUEST_UPDATE,
            nickname   = myNickname,
            questId    = questId,
            questName  = questName,
            stepIndex  = stepIndex,
            totalSteps = totalSteps,
            isComplete = isComplete });

    public void SendAISpawn(string nickname, Vector3 pos, Vector3 euler, int aiType)
        => SendTCP(new AISpawnPacket {
            type = PacketType.AI_SPAWN, nickname = nickname,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            rotX = euler.x, rotY = euler.y, rotZ = euler.z,
            aiType = aiType });

    public void SendAIDespawn(string nickname)
        => SendTCP(new DespawnPacket { type = PacketType.AI_DESPAWN, nickname = nickname });

    public void SendAIMove(string nickname, Vector3 pos, Vector3 euler, float speed)
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new AIMovePacket
        {
            type = PacketType.AI_MOVE, nickname = nickname,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            rotX = euler.x, rotY = euler.y, rotZ = euler.z,
            speed = speed, isMove = speed > 0.5f
        }));
        try { udp?.Send(data, data.Length); }
        catch { }
    }

    public void SendCockpitStateTCP(bool boarded, Vector3 pos, Vector3 rot)
        => SendTCP(new CockpitStatePacket {
            type               = PacketType.COCKPIT_STATE,
            isBoardedInCockpit = boarded,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            rotX = rot.x, rotY = rot.y, rotZ = rot.z });

    public void SendBoardAircraft(int aircraftId)
        => SendTCP(new AircraftBoardPacket { type = PacketType.BOARD_AIRCRAFT, aircraftId = aircraftId });

    public void SendLeaveAircraft(int aircraftId)
        => SendTCP(new AircraftBoardPacket { type = PacketType.LEAVE_AIRCRAFT, aircraftId = aircraftId });

    public void SendGunHit(string shooterNick, string targetNick, Vector3 pos)
        => SendTCP(new GunHitPacket {
            type            = PacketType.GUN_HIT,
            shooterNickname = shooterNick,
            targetNickname  = targetNick,
            posX = pos.x, posY = pos.y, posZ = pos.z });

    public void SendMissileMove(string id, Vector3 pos, Vector3 euler, float speed)
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new MissileMovePacket
        {
            type = PacketType.MISSILE_MOVE, missileId = id,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            rotX = euler.x, rotY = euler.y, rotZ = euler.z,
            speed = speed
        }));
        try { udp?.Send(data, data.Length); }
        catch { }
    }

    public void SendUdpConnect()
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
            new UdpConnectPacket { type = PacketType.UDP_CONNECT, nickname = myNickname }));
        try { udp?.Send(data, data.Length); }
        catch { }
    }

    void SendTCP(object packet)
    {
        if (stream == null) return;
        byte[] json   = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
        byte[] length = BitConverter.GetBytes(json.Length);
        lock (sendLock)
        {
            stream.Write(length, 0, 4);
            stream.Write(json, 0, json.Length);
        }
    }

    static void ReadFull(NetworkStream stream, byte[] buf, int size)
    {
        int offset = 0;
        while (offset < size)
        {
            int n = stream.Read(buf, offset, size - offset);
            if (n <= 0) throw new IOException("Connection closed");
            offset += n;
        }
    }

    public bool IsConnected() => socket != null && socket.Connected && stream != null;

    public void Disconnect()
    {
        if (!IsConnected()) return;
        isRunning = false;
        lastTicks.Clear();
        string nick = myNickname;
        myNickname = "";
        if (GameManager.Instance != null) GameManager.Instance.myNickname = "";
        try { SendTCP(new Packet { type = PacketType.DISCONNECT }); } catch { }
        try { stream?.Close(); } catch { }
        try { socket?.Close(); } catch { }
        try { udp?.Close(); } catch { }
        stream = null;
        socket = null;
        udp    = null;
        Debug.Log($"[Net] Disconnected ({nick})");
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        try { SendTCP(new ChatPacket { type = PacketType.DISCONNECT, message = "" }); } catch { }
        stream?.Close();
        socket?.Close();
        udp?.Close();
    }
}
