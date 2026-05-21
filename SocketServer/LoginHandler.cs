using Newtonsoft.Json;

class LoginHandler
{
    public static void Handle(ClientSession session, string json)
    {
        LoginPacket packet =
            JsonConvert.DeserializeObject<LoginPacket>(json);

        // 이미 로그인 상태인지 체크
        if (session.isLogin)
        {
            ServerSender.SendLoginResult(
                session,
                false,
                "Already Login");

            return;
        }

        // 계정 검증
        bool success =
            AccountManager.Login(
                packet.id,
                packet.password);

        if (success == false)
        {
            ServerSender.SendLoginResult(
                session,
                false,
                "Login Failed");

            return;
        }

        // 중복 접속 체크
        if (SessionManager.Find(packet.id) != null)
        {
            ServerSender.SendLoginResult(
                session,
                false,
                "Already Connected");

            return;
        }
        
        session.isLogin = true;

        Player player = new();
        // 세션 정보 저장
        player.nickname = packet.id;
        player.session = session;
        session.player = player;
        
        // 기본 Room 입장
        // Room room = RoomManager.GetRoom(1);
        
        // 테스트 Room 분리 입장
        Room room;
        if(packet.id == "test")
            room = RoomManager.GetRoom(2);
        else
            room = RoomManager.GetRoom(1);
        
        // 추후 삭제
        // 자동 입장이 아닌 클라가 선택해서 방에 입장할 것임
        //  room.Enter(player);

        // 로그인 결과 전송
        ServerSender.SendLoginResult(
            session,
            true,
            "Login Success");

        Console.WriteLine($"{session.player.nickname} login success");
    }
}