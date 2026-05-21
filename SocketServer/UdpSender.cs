using System.Net;
using System.Text;
using Newtonsoft.Json;

class UdpSender
{
    public static void Send(IPEndPoint endPoint, object packet)
    {
        if(endPoint == null)
        {
            Console.WriteLine("UDP EndPoint NULL");

            return;
        }

        string json = JsonConvert.SerializeObject(packet);

        byte[] data = Encoding.UTF8.GetBytes(json);

        Program.udpServer.Send(
            data,
            data.Length,
            endPoint);

        Console.WriteLine(
            $"UDP SEND : {json}");
    }
}