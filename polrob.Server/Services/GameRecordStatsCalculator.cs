using polrob.Shared;

public static class GameRecordStatsCalculator
{
    public static PlayerGameStats Calculate(IEnumerable<PlayerGameOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var accumulator = new GameRecordStatsAccumulator();
        foreach (var outcome in outcomes)
        {
            accumulator.Add(outcome);
        }

        return accumulator.Build();
    }
}

public sealed class GameRecordStatsAccumulator
{
    private int _totalGames;
    private int _wins;
    private int _policeGames;
    private int _policeWins;
    private int _robberGames;
    private int _robberWins;

    public void Add(PlayerGameOutcome outcome)
    {
        if (!Enum.IsDefined(outcome.PlayerRole) || !Enum.IsDefined(outcome.WinnerRole))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                "Player and winner roles must be supported player roles.");
        }

        var won = outcome.PlayerRole == outcome.WinnerRole;
        _totalGames++;
        _wins += won ? 1 : 0;

        if (outcome.PlayerRole == PlayerRole.Police)
        {
            _policeGames++;
            _policeWins += won ? 1 : 0;
        }
        else if (outcome.PlayerRole == PlayerRole.Robber)
        {
            _robberGames++;
            _robberWins += won ? 1 : 0;
        }
    }

    public PlayerGameStats Build()
    {
        return new PlayerGameStats
        {
            Overall = CreateBreakdown(_totalGames, _wins),
            Police = CreateBreakdown(_policeGames, _policeWins),
            Robber = CreateBreakdown(_robberGames, _robberWins)
        };
    }

    private static GameStatsBreakdown CreateBreakdown(int totalGames, int wins) => new()
    {
        TotalGames = totalGames,
        Wins = wins,
        Losses = totalGames - wins,
        WinRate = totalGames == 0 ? 0d : wins * 100d / totalGames
    };
}
