namespace polrob.Shared;

public class GameStateSync
{
    public string RoomId { get; set; } = string.Empty;
    public GamePhase Phase { get; set; }
    public int CountdownTime { get; set; } // 3, 2, 1
    public int GameTime { get; set; } // 300, 299...
    public PlayerRole? WinnerRole { get; set; }
    public int ElapsedGameTime { get; set; }
}
