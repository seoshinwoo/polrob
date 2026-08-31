namespace polrob.Shared;

public class GameStateSync
{
    public string RoomId { get; set; } = string.Empty;
    public GamePhase Phase { get; set; }
    public int CountdownTime { get; set; } // 3, 2, 1
    public int GameTime { get; set; } // 300, 299...
    public PlayerRole? WinnerRole { get; set; }
    public int ElapsedGameTime { get; set; }
    // 시야 필터링된 클라이언트 플레이어 목록과 무관한 서버 권위 수치입니다.
    public int TotalRobbers { get; set; }
    public int JailedRobbers { get; set; }
}
