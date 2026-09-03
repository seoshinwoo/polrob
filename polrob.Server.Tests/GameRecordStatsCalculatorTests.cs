using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRecordStatsCalculatorTests
{
    [Test]
    public void Calculate_WithNoGames_ReturnsZeroForEveryBreakdown()
    {
        var result = GameRecordStatsCalculator.Calculate(Array.Empty<PlayerGameOutcome>());

        Assert.Multiple(() =>
        {
            AssertBreakdown(result.Overall, totalGames: 0, wins: 0, losses: 0, winRate: 0d);
            AssertBreakdown(result.Police, totalGames: 0, wins: 0, losses: 0, winRate: 0d);
            AssertBreakdown(result.Robber, totalGames: 0, wins: 0, losses: 0, winRate: 0d);
        });
    }

    [Test]
    public void Calculate_AggregatesOverallAndRoleSpecificResults()
    {
        var outcomes = new[]
        {
            new PlayerGameOutcome(PlayerRole.Police, PlayerRole.Police),
            new PlayerGameOutcome(PlayerRole.Police, PlayerRole.Robber),
            new PlayerGameOutcome(PlayerRole.Robber, PlayerRole.Robber),
            new PlayerGameOutcome(PlayerRole.Robber, PlayerRole.Police),
            new PlayerGameOutcome(PlayerRole.Robber, PlayerRole.Robber)
        };

        var result = GameRecordStatsCalculator.Calculate(outcomes);

        Assert.Multiple(() =>
        {
            AssertBreakdown(result.Overall, totalGames: 5, wins: 3, losses: 2, winRate: 60d);
            AssertBreakdown(result.Police, totalGames: 2, wins: 1, losses: 1, winRate: 50d);
            AssertBreakdown(result.Robber, totalGames: 3, wins: 2, losses: 1, winRate: 200d / 3d);
        });
    }

    [Test]
    public void Calculate_WithInvalidRole_RejectsTheOutcome()
    {
        var invalidOutcome = new PlayerGameOutcome(
            (PlayerRole)999,
            PlayerRole.Police);

        Assert.That(
            () => GameRecordStatsCalculator.Calculate(new[] { invalidOutcome }),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static void AssertBreakdown(
        GameStatsBreakdown actual,
        int totalGames,
        int wins,
        int losses,
        double winRate)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.TotalGames, Is.EqualTo(totalGames));
            Assert.That(actual.Wins, Is.EqualTo(wins));
            Assert.That(actual.Losses, Is.EqualTo(losses));
            Assert.That(actual.WinRate, Is.EqualTo(winRate).Within(0.000_001d));
        });
    }
}
