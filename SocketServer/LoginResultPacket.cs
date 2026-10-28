[System.Serializable]
public class LoginResultPacket : Packet
{
    public bool success;

    public string message = string.Empty;
    public string sessionToken = string.Empty;   // UDP_CONNECT 인증용 (v4.3 N-2)
}