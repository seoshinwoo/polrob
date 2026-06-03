namespace polrob.Shared;

public class ServerResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? RoomId { get; set; }
    public PlayerRole? Role { get; set; }
    public int CurrentCount { get; set; }
    public int MaxCount { get; set; }
    public bool CreatedRoom { get; set; }
    public bool Matched { get; set; }
}
