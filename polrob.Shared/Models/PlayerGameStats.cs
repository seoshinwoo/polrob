namespace polrob.Shared;

public sealed class PlayerGameStats
{
    public GameStatsBreakdown Overall { get; init; } = new();
    public GameStatsBreakdown Police { get; init; } = new();
    public GameStatsBreakdown Robber { get; init; } = new();
}

public sealed class GameStatsBreakdown
{
    public int TotalGames { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public double WinRate { get; init; }
}
