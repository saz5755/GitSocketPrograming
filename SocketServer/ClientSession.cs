using System.Net;
using System.Net.Sockets;

public class ClientSession
{
    public TcpClient client = null!;
    public IPEndPoint? udpEndPoint;

    public NetworkStream stream = null!;

    public bool isLogin;

    public Player? player;

    public readonly object sendLock = new();
}