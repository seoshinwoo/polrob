namespace polrob.Shared;

public class GameStateSync
{
    public int Phase { get; set; } // 0=Waiting, 1=Countdown, 2=Playing, 3=Ended
    public int CountdownTime { get; set; } // 3, 2, 1
    public int GameTime { get; set; } // 300, 299...
}