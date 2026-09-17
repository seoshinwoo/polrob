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

    // 같은 roomId를 재경기에 재사용해도 이전 경기의 보이스 토큰이 새 채널에 들어오지 못하게 합니다.
    // 서버 내부 채널 구분자이므로 로비/게임 상태 응답에는 포함하지 않습니다.
    [System.Text.Json.Serialization.JsonIgnore]
    public string VoiceSessionId { get; set; } = string.Empty;

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
