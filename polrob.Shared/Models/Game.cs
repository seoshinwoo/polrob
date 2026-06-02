namespace polrob.Shared;

public class Game
{
    public string Id { get; set; }
    public string Type { get; set; }
    public List<Player> Players { get; set; }
    public bool IsOnGame { get; set; } = false;
    public Game(string type)
    {
        Id = Guid.NewGuid().ToString();
        Type = type;
        Players = new List<Player>();
    }
}