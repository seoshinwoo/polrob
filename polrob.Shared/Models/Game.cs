namespace polrob.Shared;

public class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RoomCode { get; set; } = string.Empty;
    public string Type { get; set; } = "custom";
    public bool IsPrivate { get; set; }
    public string HostUserId { get; set; } = string.Empty;
    public List<Player> Players { get; set; } = new();
    public bool IsOnGame { get; set; } = false;
    public DateTime? EmptyRoomExpiresAtUtc { get; set; }

    public Game()
    {
    }

    public Game(string type, bool isPrivate = false)
    {
        Id = Guid.NewGuid().ToString();
        Type = type;
        IsPrivate = isPrivate;
        Players = new List<Player>();
    }
}
