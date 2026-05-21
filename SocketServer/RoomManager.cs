public class RoomManager
{
    static Dictionary<int, Room> rooms = new();

    public static void Init()
    {
        CreateRoom(1);
        CreateRoom(2);
        CreateRoom(3);
    }

    public static Room CreateRoom(int roomId)
    {
        Room room = new Room();

        room.roomId = roomId;

        rooms.Add(roomId, room);

        Console.WriteLine(
            $"Room Create : {roomId}");

        return room;
    }


    public static Room GetRoom(int roomId)
    {
        if (rooms.ContainsKey(roomId))
            return rooms[roomId];

        return null;
    }
}