using System.Collections.Generic;

// 사망 보고 (TCP) — 피격자 소유 클라이언트(플레이어 본인 / AI 는 호스트)가 송신, 서버가 방 전체에 중계
public class PlayerKilledPacket : Packet
{
    public string victim  = "";
    public string killer  = "";   // 없으면 "" (추락·사출 등 자멸)
    public string weapon  = "";   // MSL / GUN
    public string cause   = "";   // DeathCause 문자열
    public float  posX, posY, posZ;
    public int    aircraftId = -1;
}

public class ScoreEntry
{
    public string nickname = "";
    public int    kills;
    public int    aiKills;
    public int    deaths;
}

// 서버 → 클라: 방 전과 (입장·퇴장·격추 시 갱신)
public class ScoreboardPacket : Packet
{
    public List<ScoreEntry> entries = new();
}

// 기체 상태 (TCP) — state 0: 잔해 소멸(숨김), 1: 갑판 슬롯으로 재배치(새 출격 가능)
public class AircraftStatePacket : Packet
{
    public string nickname = "";
    public int    aircraftId = -1;
    public int    state;
    public float  posX, posY, posZ;
}
