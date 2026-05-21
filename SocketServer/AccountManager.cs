using System.Collections.Generic;

public static class AccountManager
{
    static Dictionary<string, string> accounts = new()
        {
            { "hj", "1234" },
            { "jh", "1234" },
            { "yg", "1234" },
            { "test", "1111" },
            { "test2", "1111" },
            { "test3", "1111" },
        };

    public static bool Login(string id, string pw)
    {
        if (!accounts.ContainsKey(id))
            return false;

        return accounts[id] == pw;
    }
}