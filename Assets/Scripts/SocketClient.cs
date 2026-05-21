using System;
using System.Collections;
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
    TcpClient socket;
    UdpClient udp;
    NetworkStream stream;
    Thread receiveThread;
    Thread udpReceiveThread;

    public string myNickname;
    public ChatUIManager uiManager;
    public PlayerManager playerManager;

    volatile bool isRunning = true;

    [Header("Connection")]
    public string connectIP = "172.30.201.117";
    public int tcpPort = 5000;
    public int udpPort = 6000;
    public int defaultRoomId = 1;

    // ── 연결 ──────────────────────────────────────────────────────────────
    public void Connect()
    {
        socket = new TcpClient(connectIP, tcpPort);
        stream = socket.GetStream();

        receiveThread = new Thread(ReceiveMessage) { IsBackground = true };
        receiveThread.Start();

        udp = new UdpClient(0);
        udp.Connect(connectIP, udpPort);

        udpReceiveThread = new Thread(UdpReceiveLoop) { IsBackground = true };
        udpReceiveThread.Start();
    }

    // ── TCP 수신 루프 ──────────────────────────────────────────────────────
    void ReceiveMessage()
    {
        byte[] lengthBuffer = new byte[4];

        while (isRunning)
        {
            try
            {
                ReadFull(stream, lengthBuffer, 4);
                int length = BitConverter.ToInt32(lengthBuffer, 0);

                byte[] dataBuffer = new byte[length];
                ReadFull(stream, dataBuffer, length);

                string json = Encoding.UTF8.GetString(dataBuffer);
                JObject obj = JObject.Parse(json);
                PacketType type = (PacketType)obj["type"].Value<int>();

                HandleTCP(type, json);
            }
            catch (IOException)
            {
                Debug.Log("[TCP] Disconnected");
                break;
            }
            catch (SocketException)
            {
                Debug.Log("[TCP] Socket closed");
                break;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCP] {e}");
            }
        }
    }

    void HandleTCP(PacketType type, string json)
    {
        switch (type)
        {
            case PacketType.LOGIN_RESULT:
            {
                LoginResultPacket packet = JsonConvert.DeserializeObject<LoginResultPacket>(json);

                if (packet.success)
                {
                    UnityMainThreadDispatcher.Instance.Enqueue(() =>
                    {
                        uiManager.SetChatEnable(true);
                        // UDP_CONNECT 먼저 전송 후 일정 시간 뒤 EnterRoom
                        StartCoroutine(RegisterAndEnterRoom());
                    });
                }
                else
                {
                    Debug.Log($"[Login] Failed: {packet.message}");
                }
                break;
            }

            case PacketType.CHAT:
            case PacketType.SYSTEM:
            {
                ChatPacket packet = JsonConvert.DeserializeObject<ChatPacket>(json);
                string msg = packet.type == PacketType.CHAT
                    ? $"[{packet.nickname}] {packet.message}"
                    : $"<color=yellow>{packet.message}</color>";

                UnityMainThreadDispatcher.Instance.Enqueue(() => uiManager.AddMessage(msg));
                break;
            }

            case PacketType.SPAWN:
            {
                SpawnPacket packet = JsonConvert.DeserializeObject<SpawnPacket>(json);
                Vector3 pos = new Vector3(packet.x, packet.y, packet.z);
                Quaternion rot = Quaternion.Euler(packet.rotX, packet.rotY, packet.rotZ);

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    playerManager.CreatePlayer(packet.nickname, pos, rot, packet.isMove);
                });
                break;
            }

            case PacketType.DESPAWN:
            {
                DespawnPacket packet = JsonConvert.DeserializeObject<DespawnPacket>(json);

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    playerManager.RemovePlayer(packet.nickname);
                });
                break;
            }

            // TCP MOVE: UDP endpoint 등록 전 fallback으로 수신
            case PacketType.MOVE:
            {
                MoveBroadcastPacket packet = JsonConvert.DeserializeObject<MoveBroadcastPacket>(json);

                // 자신의 패킷 무시
                if (packet.nickname == myNickname)
                    break;

                Vector3 pos = new Vector3(packet.posX, packet.posY, packet.posZ);
                Quaternion rot = Quaternion.Euler(packet.rotX, packet.rotY, packet.rotZ);
                int tick = packet.tick;
                string nick = packet.nickname;
                bool isMove = packet.isMove;

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    playerManager.MovePlayer(nick, pos, rot, isMove, tick);
                });
                break;
            }

            default:
                break;
        }
    }

    // ── UDP_CONNECT 전송 후 300ms 대기, 그 뒤 EnterRoom ──────────────────
    IEnumerator RegisterAndEnterRoom()
    {
        // UDP_CONNECT 여러 번 전송 (패킷 손실 보완)
        SendUdpConnect();
        yield return new WaitForSeconds(0.1f);
        SendUdpConnect();
        yield return new WaitForSeconds(0.2f);

        EnterRoom(defaultRoomId);

        // 주기적으로 UDP_CONNECT 재전송 (다른 클라이언트가 나중에 접속할 때 대비)
        StartCoroutine(KeepUdpAlive());
    }

    IEnumerator KeepUdpAlive()
    {
        while (IsConnected())
        {
            yield return new WaitForSeconds(5f);
            if (IsConnected())
                SendUdpConnect();
        }
    }

    // ── UDP 수신 루프 ──────────────────────────────────────────────────────
    void UdpReceiveLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                byte[] data = udp.Receive(ref remoteEP);
                string json = Encoding.UTF8.GetString(data);
                JObject obj = JObject.Parse(json);
                PacketType type = (PacketType)obj["type"].Value<int>();

                if (type != PacketType.MOVE)
                    continue;

                MoveBroadcastPacket packet = JsonConvert.DeserializeObject<MoveBroadcastPacket>(json);

                // 자신의 패킷 무시
                if (packet.nickname == myNickname)
                    continue;

                PlayerController player = playerManager.GetPlayer(packet.nickname);
                if (player == null)
                    continue;

                // 오래된 패킷 무시
                if (packet.tick > 0 && packet.tick <= player.lastReceivedTick)
                    continue;

                player.lastReceivedTick = packet.tick;

                Vector3 pos = new Vector3(packet.posX, packet.posY, packet.posZ);
                Quaternion rot = Quaternion.Euler(packet.rotX, packet.rotY, packet.rotZ);
                bool isMove = packet.isMove;
                int tick = packet.tick;
                string nick = packet.nickname;

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    playerManager.MovePlayer(nick, pos, rot, isMove, tick);
                });
            }
            catch (Exception e)
            {
                if (isRunning)
                    Debug.LogError($"[UDP] {e.Message}");
            }
        }
    }

    // ── 송신 메서드 ────────────────────────────────────────────────────────
    public void Login(string id, string pw)
    {
        if (stream == null)
            Connect();

        myNickname = id;

        LoginPacket packet = new()
        {
            type = PacketType.LOGIN,
            id = id,
            password = pw
        };

        SendPacket(packet);
    }

    public void SendChat(string msg)
    {
        ChatPacket packet = new()
        {
            type = PacketType.CHAT,
            message = msg
        };

        SendPacket(packet);
    }

    public void EnterRoom(int roomId)
    {
        EnterRoomPacket packet = new()
        {
            type = PacketType.ENTER_ROOM,
            roomId = roomId
        };

        SendPacket(packet);
    }

    public void SendMove(
        float posX, float posY, float posZ,
        float rotX, float rotY, float rotZ,
        bool isMove, int tick)
    {
        MovePacket packet = new()
        {
            type = PacketType.MOVE,
            tick = tick,
            posX = posX, posY = posY, posZ = posZ,
            rotX = rotX, rotY = rotY, rotZ = rotZ,
            isMove = isMove
        };

        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
        udp.Send(data, data.Length);
    }

    public void SendUdpConnect()
    {
        UdpConnectPacket packet = new()
        {
            type = PacketType.UDP_CONNECT,
            nickname = myNickname
        };

        byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
        udp.Send(data, data.Length);

        Debug.Log($"[UDP] Connect sent: {myNickname}");
    }

    void SendDisconnect()
    {
        if (socket == null) return;

        ChatPacket packet = new()
        {
            type = PacketType.DISCONNECT,
            message = ""
        };

        try { SendPacket(packet); }
        catch { }
    }

    void SendPacket(object packet)
    {
        string json = JsonConvert.SerializeObject(packet);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] length = BitConverter.GetBytes(jsonBytes.Length);

        stream.Write(length, 0, 4);
        stream.Write(jsonBytes, 0, jsonBytes.Length);
    }

    static void ReadFull(NetworkStream stream, byte[] buffer, int size)
    {
        int offset = 0;
        while (offset < size)
        {
            int read = stream.Read(buffer, offset, size - offset);
            if (read <= 0)
                throw new IOException("Connection closed");
            offset += read;
        }
    }

    public bool IsConnected() => socket != null && socket.Connected && stream != null;

    void OnApplicationQuit()
    {
        isRunning = false;
        SendDisconnect();
        stream?.Close();
        socket?.Close();
        udp?.Close();
    }
}
