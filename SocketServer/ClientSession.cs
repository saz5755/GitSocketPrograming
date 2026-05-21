using System.Net;
using System.Net.Sockets;

public class ClientSession
{
    public TcpClient client;
    public IPEndPoint udpEndPoint;
    
    public NetworkStream stream;
    
    public bool isLogin;

    public Player player;
}