public class RoomManager
{
    static Dictionary<int, Room> rooms = new();

    static readonly string[] RoomNames = { "Combat Zone Alpha", "Combat Zone Bravo", "Combat Zone Charlie" };

    public static void Init()
    {
        CreateRoom(1);
        CreateRoom(2);
        CreateRoom(3);
    }

    public static IEnumerable<Room> GetAllRooms() => rooms.Values;

    public static Room CreateRoom(int roomId)
    {
        Room room = new Room();
        room.roomId = roomId;
        room.roomName = roomId <= RoomNames.Length ? RoomNames[roomId - 1] : $"Zone {roomId:D2}";

        rooms.Add(roomId, room);

        Console.WriteLine(
            $"Room Create : {roomId}");

        return room;
    }


    public static Room? GetRoom(int roomId)
    {
        if (rooms.ContainsKey(roomId))
            return rooms[roomId];

        return null;
    }
}