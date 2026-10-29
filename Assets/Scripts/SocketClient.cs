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

    // 세션 토큰 (LOGIN_RESULT) — UDP_CONNECT 에 실어 보내야 서버가 UDP 엔드포인트를 등록한다
    string sessionToken = "";

    // 재접속 (v4.3 N-5) — 로그인 자격은 메모리에만 보관
    string _loginId, _loginPw;
    public bool CanReconnect => !string.IsNullOrEmpty(_loginId);
    /// <summary>의도하지 않은 TCP 끊김 (메인 스레드)</summary>
    public event Action OnConnectionLost;

    [Header("Connection Defaults")]
    public string connectIP = "172.30.201.117";
    public int    tcpPort   = 5000;
    public int    udpPort   = 6000;

    // UDP 틱 추적 (스레드 안전, PlayerManager 불필요)
    readonly ConcurrentDictionary<string, int> lastTicks = new();

    // 아웃바운드 MOVE 틱 카운터 — Ground/Flight 공용, 단조 증가로 중복 패킷 차단
    int _outTick = 0;
    public int GetNextTick() => ++_outTick;

    // 현재 방의 호스트 닉네임 — ENTER_ROOM_RESULT 수신 즉시 세팅 (씬 전환 전에도 참조 가능)
    public string roomHostNickname = "";
    public string roomMode = "";
    public int    roomEnemySkill = -1;   // 적 AI 숙련도 (0~2, 모르면 -1) — ENTER_ROOM_RESULT / ROOM_SETTING 으로 수신          // 현재 방 모드 (RoomModeSO.Airshow / Combat) — ENTER_ROOM_RESULT 로 수신
    public ScoreboardPacket LastScoreboard;

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
    public event Action<CountermeasurePacket>   OnCountermeasure;
    public event Action<RoomSettingPacket>      OnRoomSetting;
    public event Action<CreateRoomResultPacket> OnCreateRoomResult;
    public event Action<EnterRoomResultPacket>  OnEnterRoomResult;
    public event Action<QuestUpdatePacket>      OnQuestUpdate;
    public event Action<AISpawnPacket>          OnAISpawn;
    public event Action<string>                 OnAIDespawn;
    public event Action<AIMovePacket>           OnAIMove;
    public event Action<HostChangePacket>       OnHostChange;
    public event Action<AircraftBoardPacket>      OnAircraftBoard;
    public event Action<AircraftBoardPacket>      OnAircraftLeave;
    public event Action<AircraftPoolSyncPacket>   OnAircraftPoolSync;
    public event Action<ArrestEventPacket>        OnArrestEvent;
    public event Action<PlayerKilledPacket>       OnPlayerKilled;
    public event Action<ScoreboardPacket>         OnScoreboard;
    public event Action<AircraftStatePacket>      OnAircraftState;

    // ── 연결 ──────────────────────────────────────────────────────────────
    public void Connect(string ip = null)
    {
        if (IsConnected()) return;
        // 끊긴 이전 연결이 남아 있으면 정리 — 안 그러면 옛 UDP 소켓·수신 스레드가 누수된다
        if (socket != null || udp != null) Disconnect();

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
        var s = stream;   // 재접속으로 필드가 교체돼도 이 스레드는 자기 스트림만 읽는다
        bool lost = false;
        while (isRunning && s == stream)
        {
            try
            {
                ReadFull(s, lenBuf, 4);
                int length = BitConverter.ToInt32(lenBuf, 0);
                byte[] dataBuf = new byte[length];
                ReadFull(s, dataBuf, length);
                string json = Encoding.UTF8.GetString(dataBuf);
                HandleTCP(ReadPacketType(json), json);
            }
            catch (IOException)     { Debug.Log("[TCP] Disconnected"); lost = true; break; }
            catch (SocketException) { Debug.Log("[TCP] Socket closed"); lost = true; break; }
            catch (ObjectDisposedException) { lost = true; break; }
            catch (Exception e)  { if (isRunning) Debug.LogError($"[TCP] {e}"); }
        }
        // 서버가 끊었거나 네트워크가 떨어짐 — 의도한 Disconnect() 는 isRunning 을 먼저 내리므로 제외
        if (lost && isRunning && s == stream)
            UnityMainThreadDispatcher.Instance.Enqueue(() => OnConnectionLost?.Invoke());
    }

    void HandleTCP(PacketType type, string json)
    {
        switch (type)
        {
            case PacketType.LOGIN_RESULT:
            {
                var p = JsonConvert.DeserializeObject<LoginResultPacket>(json);
                if (p.success) sessionToken = p.sessionToken ?? "";
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

            case PacketType.ROOM_SETTING:
            {
                var p = JsonConvert.DeserializeObject<RoomSettingPacket>(json);
                if (p == null) break;
                roomEnemySkill = p.enemySkill;
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnRoomSetting?.Invoke(p));
                break;
            }

            case PacketType.COUNTERMEASURE:
            {
                var p = JsonConvert.DeserializeObject<CountermeasurePacket>(json);
                if (p == null || p.ownerNickname == myNickname) break;
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnCountermeasure?.Invoke(p));
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
                // 씬 전환 전(수신 스레드)에 즉시 저장 — AIManager.TryInitializeAsHost가 씬 로드 후 참조
                if (p.success) { roomHostNickname = p.hostNickname ?? ""; roomMode = p.mode ?? ""; roomEnemySkill = p.enemySkill; }
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
                if (p != null && !string.IsNullOrEmpty(p.newHostNickname)) roomHostNickname = p.newHostNickname;   // 방장 권한 UI(ESC 메뉴)가 참조
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

            case PacketType.AIRCRAFT_POOL_SYNC:
            {
                var p = JsonConvert.DeserializeObject<AircraftPoolSyncPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnAircraftPoolSync?.Invoke(p));
                break;
            }

            case PacketType.PLAYER_KILLED:
            {
                var p = JsonConvert.DeserializeObject<PlayerKilledPacket>(json);
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnPlayerKilled?.Invoke(p));
                break;
            }

            case PacketType.SCOREBOARD:
            {
                var p = JsonConvert.DeserializeObject<ScoreboardPacket>(json);
                LastScoreboard = p;   // UI 가 늦게 생성돼도 최신 전과를 읽을 수 있게
                UnityMainThreadDispatcher.Instance.Enqueue(() => OnScoreboard?.Invoke(p));
                break;
            }

            case PacketType.AIRCRAFT_STATE:
            {
                var p = JsonConvert.DeserializeObject<AircraftStatePacket>(json);
                if (p.nickname != myNickname)
                    UnityMainThreadDispatcher.Instance.Enqueue(() => OnAircraftState?.Invoke(p));
                break;
            }

            case PacketType.ARREST_EVENT:
            {
                var p = JsonConvert.DeserializeObject<ArrestEventPacket>(json);
                if (p.nickname != myNickname)
                    UnityMainThreadDispatcher.Instance.Enqueue(() => OnArrestEvent?.Invoke(p));
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
        var u = udp;   // 재접속 시 필드가 교체돼도 이 스레드는 자기 소켓만 사용
        while (isRunning && u != null)
        {
            try
            {
                byte[] data = u.Receive(ref ep);
                if (BinaryProtocol.IsBinary(data, data.Length))
                {
                    HandleUDPBinary(data);
                    continue;
                }
                string json = Encoding.UTF8.GetString(data);
                var type = ReadPacketType(json);

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
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Windows: 직전 송신이 ICMP Port Unreachable(서버 재시작 등)을 받으면 다음 Receive가 10054로 실패.
                // 소켓은 멀쩡하므로 계속 수신 — 여기서 break하면 이후 모든 원격 이동 동기화가 끊긴다.
                continue;
            }
            catch (SocketException se)
            {
                if (isRunning) Debug.LogError($"[UDP] {se.Message}");
                break;
            }
            catch (Exception e)
            {
                // 패킷 하나의 파싱 실패로 수신 스레드 전체가 죽지 않도록 로그만 남기고 계속
                if (isRunning) Debug.LogError($"[UDP] {e.Message}");
            }
        }
    }

    // 바이너리 UDP 수신 (v4.3 N-1) — 서버는 이 클라이언트가 바이너리를 보낸 뒤부터 바이너리로 답한다
    void HandleUDPBinary(byte[] data)
    {
        int len = data.Length;
        switch (BinaryProtocol.TypeOf(data))
        {
            case PacketType.MOVE:
            {
                var p = BinaryProtocol.ReadMove(data, len);
                if (p == null || p.nickname == myNickname) break;
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
                var p = BinaryProtocol.ReadMoveAck(data, len);
                if (p != null) UnityMainThreadDispatcher.Instance.Enqueue(() => OnMoveAck?.Invoke(p));
                break;
            }
            case PacketType.AI_MOVE:
            {
                var p = BinaryProtocol.ReadAIMove(data, len);
                if (p != null) UnityMainThreadDispatcher.Instance.Enqueue(() => OnAIMove?.Invoke(p));
                break;
            }
            case PacketType.MISSILE_MOVE:
            {
                var p = BinaryProtocol.ReadMissileMove(data, len);
                if (p != null) UnityMainThreadDispatcher.Instance.Enqueue(() => OnMissileMove?.Invoke(p));
                break;
            }
        }
    }

    // ── 송신 ──────────────────────────────────────────────────────────────
    public void Login(string id, string pw)
    {
        if (!IsConnected()) Connect();
        _loginId = id; _loginPw = pw;
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
        roomHostNickname = "";
        roomMode         = "";
        roomEnemySkill   = -1;
        _outTick = 0;
        SendTCP(new LeaveRoomPacket { type = PacketType.LEAVE_ROOM });
    }

    public void RequestRoomList()
        => SendTCP(new Packet { type = PacketType.ROOM_LIST_REQUEST });

    public void SendCreateRoom(string roomName, int maxPlayers = 8, string mode = RoomModeSO.Combat)
        => SendTCP(new CreateRoomPacket { type = PacketType.CREATE_ROOM, roomName = roomName, maxPlayers = maxPlayers, mode = mode });

    public void SendMove(float posX, float posY, float posZ,
                         float rotX, float rotY, float rotZ,
                         bool isMove, bool isFlying, int tick,
                         bool isBoardedInCockpit = false,
                         int  animState          = 0)
    {
        var u = udp;
        if (u == null) return;
        lock (sendLock)
        {
            int len = BinaryProtocol.WriteMove(_udpBuf, tick, posX, posY, posZ, rotX, rotY, rotZ,
                                               isMove, isFlying, isBoardedInCockpit, animState);
            try { u.Client.Send(_udpBuf, 0, len, SocketFlags.None); } catch { }
        }
    }

    public void SendMissileSpawn(string id, string shooter, string target,
                                 Vector3 pos, Vector3 rot, int guidanceType, string weapon = "")
        => SendTCP(new MissileSpawnPacket {
            type = PacketType.MISSILE_SPAWN, missileId = id,
            shooterNickname = shooter, targetNickname = target,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            rotX = rot.x, rotY = rot.y, rotZ = rot.z,
            guidanceType = guidanceType, weapon = weapon });

    /// <summary>방장 전용 — 서버가 권한 검증 후 방 전체에 중계</summary>
    public void SendRoomSetting(int enemySkill)
        => SendTCP(new RoomSettingPacket { type = PacketType.ROOM_SETTING, enemySkill = enemySkill });

    public void SendCountermeasure(string owner, int cmType, Vector3 pos, Vector3 vel)
        => SendTCP(new CountermeasurePacket {
            type = PacketType.COUNTERMEASURE, ownerNickname = owner, cmType = cmType,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            velX = vel.x, velY = vel.y, velZ = vel.z });

    public void SendMissileDestroy(string id, string shooterNick, string hitNick, Vector3 pos, float damage = 0f)
        => SendTCP(new MissileDestroyPacket {
            type = PacketType.MISSILE_DESTROY, missileId = id,
            shooterNickname = shooterNick, hitNickname = hitNick, damage = damage,
            posX = pos.x, posY = pos.y, posZ = pos.z });

    public void SendMissileWarn(string shooter, string target, int lockLevel)
        => SendTCP(new MissileWarnPacket {
            type = PacketType.MISSILE_WARN,
            shooterNickname = shooter, targetNickname = target,
            lockLevel = lockLevel });

    public void SendGunFire(string shooterNick, Vector3 muzzlePos, Vector3 dir)
    {
        SendUDP(new GunFirePacket
        {
            type             = PacketType.GUN_FIRE,
            shooterNickname  = shooterNick,
            posX = muzzlePos.x, posY = muzzlePos.y, posZ = muzzlePos.z,
            dirX = dir.x,       dirY = dir.y,       dirZ = dir.z
        });
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
        var u = udp;
        if (u == null) return;
        lock (sendLock)
        {
            int len = BinaryProtocol.WriteAIMove(_udpBuf, nickname, pos.x, pos.y, pos.z, euler.x, euler.y, euler.z, speed);
            try { u.Client.Send(_udpBuf, 0, len, SocketFlags.None); } catch { }
        }
    }

    public void SendCockpitStateTCP(bool boarded, Vector3 pos, Vector3 rot)
        => SendTCP(new CockpitStatePacket {
            type               = PacketType.COCKPIT_STATE,
            isBoardedInCockpit = boarded,
            posX = pos.x, posY = pos.y, posZ = pos.z,
            rotX = rot.x, rotY = rot.y, rotZ = rot.z });

    public void SendArrestEvent(int eventType, Vector3 approachDir, Vector3 wirePos)
        => SendTCP(new ArrestEventPacket {
            type         = PacketType.ARREST_EVENT,
            nickname     = myNickname,
            eventType    = eventType,
            approachDirX = approachDir.x, approachDirY = approachDir.y, approachDirZ = approachDir.z,
            wirePosX     = wirePos.x,     wirePosY     = wirePos.y,     wirePosZ     = wirePos.z });

    public void SendPlayerKilled(string victim, string killer, string weapon, string cause, Vector3 pos, int aircraftId)
        => SendTCP(new PlayerKilledPacket {
            type   = PacketType.PLAYER_KILLED,
            victim = victim, killer = killer ?? "", weapon = weapon ?? "", cause = cause,
            posX = pos.x, posY = pos.y, posZ = pos.z, aircraftId = aircraftId });

    public void SendAircraftState(int aircraftId, int state, Vector3 pos)
        => SendTCP(new AircraftStatePacket {
            type = PacketType.AIRCRAFT_STATE, nickname = myNickname,
            aircraftId = aircraftId, state = state, posX = pos.x, posY = pos.y, posZ = pos.z });

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
        var u = udp;
        if (u == null) return;
        lock (sendLock)
        {
            int len = BinaryProtocol.WriteMissileMove(_udpBuf, id, pos.x, pos.y, pos.z, euler.x, euler.y, euler.z, speed);
            try { u.Client.Send(_udpBuf, 0, len, SocketFlags.None); } catch { }
        }
    }

    public void SendUdpConnect()
    {
        SendUDP(new UdpConnectPacket { type = PacketType.UDP_CONNECT, nickname = myNickname, token = sessionToken });
    }

    // ── 직렬화 버퍼 재사용 ──────────────────────────────────────────────────
    // JsonConvert.SerializeObject는 호출마다 StringWriter·JsonTextWriter·string·byte[]를 새로 만든다
    // (패킷당 ~2.4KB). 항공기 5대 × 20Hz 송신이면 초당 수백 KB 가비지 → 주기적 GC 스파이크.
    // 직렬화기·StringBuilder·char/byte 버퍼를 재사용해 패킷 객체 외 할당을 없앤다. 출력 JSON은 동일.
    static readonly JsonSerializer s_serializer = JsonSerializer.CreateDefault();
    readonly StringBuilder _sb = new StringBuilder(256);
    StringWriter _sw;
    char[] _charBuf = new char[512];
    byte[] _byteBuf = new byte[4 + 1024];
    readonly byte[] _udpBuf = new byte[256];   // 바이너리 UDP 송신 버퍼 (sendLock 안에서만)

    // _byteBuf[4..4+len) 에 UTF-8 JSON, [0..4) 에 길이 프리픽스를 채운다. sendLock 안에서만 호출.
    int SerializeToBuffer(object packet)
    {
        _sb.Clear();
        _sw ??= new StringWriter(_sb, System.Globalization.CultureInfo.InvariantCulture);
        using (var jw = new JsonTextWriter(_sw) { CloseOutput = false })
            s_serializer.Serialize(jw, packet);

        int chars = _sb.Length;
        if (_charBuf.Length < chars) _charBuf = new char[chars * 2];
        _sb.CopyTo(0, _charBuf, 0, chars);

        int maxBytes = Encoding.UTF8.GetMaxByteCount(chars) + 4;
        if (_byteBuf.Length < maxBytes) _byteBuf = new byte[maxBytes * 2];
        int len = Encoding.UTF8.GetBytes(_charBuf, 0, chars, _byteBuf, 4);

        _byteBuf[0] = (byte)len;
        _byteBuf[1] = (byte)(len >> 8);
        _byteBuf[2] = (byte)(len >> 16);
        _byteBuf[3] = (byte)(len >> 24);
        return len;
    }

    void SendTCP(object packet)
    {
        if (stream == null) return;
        lock (sendLock)
        {
            int len = SerializeToBuffer(packet);
            stream.Write(_byteBuf, 0, 4 + len);
        }
    }

    void SendUDP(object packet)
    {
        var u = udp;
        if (u == null) return;
        lock (sendLock)
        {
            int len = SerializeToBuffer(packet);
            try { u.Client.Send(_byteBuf, 4, len, SocketFlags.None); }
            catch { }
        }
    }

    // 패킷 type 필드만 토큰 스캔으로 읽는다 — JObject 트리 전체를 만든 뒤 다시 역직렬화하던 이중 파싱 제거.
    static PacketType ReadPacketType(string json)
    {
        using (var r = new JsonTextReader(new StringReader(json)))
        {
            while (r.Read())
            {
                if (r.TokenType == JsonToken.PropertyName && r.Depth == 1 && (string)r.Value == "type")
                    return (PacketType)(r.ReadAsInt32() ?? 0);
                if (r.TokenType == JsonToken.StartObject || r.TokenType == JsonToken.StartArray)
                {
                    if (r.Depth >= 1) r.Skip();
                }
            }
        }
        return 0;
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

    /// <summary>
    /// 끊긴 연결을 버리고 같은 자격으로 다시 접속·로그인한다 (메인 스레드). 결과는 OnLoginResult 로 온다.
    /// 닉네임·방 정보는 유지 — 성공하면 호출 측이 같은 방에 다시 입장한다.
    /// </summary>
    public void Reconnect()
    {
        if (!CanReconnect) return;
        isRunning = false;
        try { stream?.Close(); } catch { }
        try { socket?.Close(); } catch { }
        try { udp?.Close(); } catch { }
        stream = null; socket = null; udp = null;
        lastTicks.Clear();
        _outTick = 0;
        sessionToken = "";
        Connect();                       // 실패 시 예외 — 호출 측이 재시도
        Login(_loginId, _loginPw);
    }

    public void Disconnect()
    {
        if (socket == null && udp == null) return;
        isRunning = false;
        lastTicks.Clear();
        string nick = myNickname;
        myNickname = "";
        _loginId = _loginPw = null;
        sessionToken = "";
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
