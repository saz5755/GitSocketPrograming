using System.Net;

class SessionManager
{
    static List<ClientSession> sessions = new();

    static object locker = new();

    public static void Add(ClientSession session)
    {
        lock (locker)
        {
            sessions.Add(session);
        }

        Console.WriteLine($"Session Add : {sessions.Count}");
    }

    public static void Remove(ClientSession session)
    {
        lock (locker)
        {
            sessions.Remove(session);
        }

        Console.WriteLine($"Session Remove : {sessions.Count}");
    }

    public static List<ClientSession> GetSessions()
    {
        lock (locker)
        {
            // return session; 은
            // 외부에서 수정 가능성있으므로 복사본 리턴
            return new List<ClientSession>(sessions);
        }
    }
    
    public static ClientSession Find(string accountId)
    {
        lock (locker)
        {
            foreach (var session in sessions)
            {
                if (session.player == null)
                    continue;

                if (session.player.nickname == accountId)
                    return session;
            }

            return null;
        }
    }
    
    public static ClientSession FindByUDP(IPEndPoint ep)
    {
        foreach(var session in sessions)
        {
            if(session.udpEndPoint == null)
                continue;

            if(session.udpEndPoint.Equals(ep))
                return session;
        }

        return null;
    }
    
    public static ClientSession FindByNickname(string nickname)
    {
        foreach(var session in sessions)
        {
            if(session.player == null)
                continue;

            if(session.player.nickname == nickname)
            {
                return session;
            }
        }

        return null;
    }
}