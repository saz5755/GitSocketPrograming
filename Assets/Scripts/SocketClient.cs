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
    public event Action<string>               OnChat;       // 포맷된 문자열
    public event Action<SpawnPacket>          OnSpawn;
    public event Action<string>               OnDespawn;    // nickname
    public event Action<MoveBroadcastPacket>  OnMove;
    public event Action<List<RoomInfo>>       OnRoomList;

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
                    OnDespawn?.Invoke(p.nickname);
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

            default: break;
        }
    }

    // ── UDP: 등록 + keepalive ──────────────────────────────────────────────
    IEnumerator RegisterUDP()
    {
        SendUdpConnect();
        yield return new WaitForSeconds(0.1f);
        SendUdpConnect();
        yield return new WaitForSeconds(0.2f);
        StartCoroutine(KeepUdpAlive());
    }

    IEnumerator KeepUdpAlive()
    {
        while (IsConnected())
        {
            yield return new WaitForSeconds(5f);
            if (IsConnected()) SendUdpConnect();
        }
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
                if ((PacketType)obj["type"].Value<int>() != PacketType.MOVE) continue;

                var p = JsonConvert.DeserializeObject<MoveBroadcastPacket>(json);
                if (p.nickname == myNickname) continue;

                // 오래된 패킷 드롭 (PlayerManager 의존 없음)
                if (p.tick > 0)
                {
                    int last = lastTicks.GetOrAdd(p.nickname, 0);
                    if (p.tick <= last) continue;
                    lastTicks[p.nickname] = p.tick;
                }

                UnityMainThreadDispatcher.Instance.Enqueue(() => OnMove?.Invoke(p));
            }
            catch (Exception e)
            {
                if (isRunning) Debug.LogError($"[UDP] {e.Message}");
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
        => SendTCP(new EnterRoomPacket { type = PacketType.ENTER_ROOM, roomId = roomId });

    public void LeaveRoom()
    {
        lastTicks.Clear();
        SendTCP(new LeaveRoomPacket { type = PacketType.LEAVE_ROOM });
    }

    public void RequestRoomList()
        => SendTCP(new Packet { type = PacketType.ROOM_LIST_REQUEST });

    public void SendMove(float posX, float posY, float posZ,
                         float rotX, float rotY, float rotZ,
                         bool isMove, int tick)
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new MovePacket
        {
            type = PacketType.MOVE, tick = tick,
            posX = posX, posY = posY, posZ = posZ,
            rotX = rotX, rotY = rotY, rotZ = rotZ,
            isMove = isMove
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
