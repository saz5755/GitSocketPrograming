/// <summary>
/// 서버가 룸 단위로 유지하는 항공기 상태 레코드.
/// 플레이어 세션과 독립적으로 존재한다.
/// </summary>
public class AircraftEntry
{
    public string nickname           = "";  // AI 닉 or 플레이어닉 (풀 키)
    public int    networkId          = -1;  // NetworkAircraft.networkId (-1=미등록)
    public float  posX, posY, posZ;
    public float  rotX, rotY, rotZ;
    public float  speed;
    public bool   isFlying;
    public int    aiType             = -1;  // -1=플레이어기체, 0=에스코트봇, 1=에너미봇
    public string controllerNickname = "";  // 현재 조종 중인 플레이어 닉 (없으면 "")
}
