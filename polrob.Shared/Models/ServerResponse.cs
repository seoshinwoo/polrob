namespace polrob.Shared;

public class ServerResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? RoomId { get; set; }
    public string? RoomCode { get; set; }
    public PlayerRole? Role { get; set; }
    public int CurrentCount { get; set; } // 현재 방에 들어있는 플레이어 수
    public int MaxCount { get; set; }
    public bool CreatedRoom { get; set; }
    public bool Matched { get; set; }
    public bool IsPrivate { get; set; }
    public List<Player> Players { get; set; } = new();
}
