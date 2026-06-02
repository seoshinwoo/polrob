namespace polrob.Shared;

public enum PlayerRole
{
    Police,
    Robber
}

public class Player
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Speed { get; set; } = 7f;
    public float Radius { get; set; } = 50f;
    public float Angle { get; set; }
    public bool IsMoving { get; set; }
    public PlayerRole Role { get; set; }
}