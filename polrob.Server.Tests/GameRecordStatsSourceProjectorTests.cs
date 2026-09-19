using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRecordStatsSourceProjectorTests
{
    private const string UserId = "profile-user";

    [TestCase(PlayerRole.Police)]
    [TestCase(PlayerRole.Robber)]
    public void TryCreateOutcome_UsesRoleMembershipFromCanonicalRecord(PlayerRole playerRole)
    {
        var policePlayerIds = playerRole == PlayerRole.Police ? new[] { UserId } : Array.Empty<string>();
        var robberPlayerIds = playerRole == PlayerRole.Robber ? new[] { UserId } : Array.Empty<string>();

        var created = GameRecordStatsSourceProjector.TryCreateOutcome(
            UserId,
            PlayerRole.Police.ToString(),
            policePlayerIds,
            robberPlayerIds,
            out var outcome,
            out var failure);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(failure, Is.EqualTo(GameRecordStatsProjectionFailure.None));
            Assert.That(outcome.PlayerRole, Is.EqualTo(playerRole));
            Assert.That(outcome.WinnerRole, Is.EqualTo(PlayerRole.Police));
        });
    }

    [Test]
    public void TryCreateOutcome_LegacyCanonicalRecord_DoesNotRequireDerivedPlayerIndex()
    {
        // Legacy source records can say their old-container index is complete, but
        // the canonical role arrays remain sufficient to rebuild the outcome.
        var created = GameRecordStatsSourceProjector.TryCreateOutcome(
            UserId,
            "Robber",
            new[] { "another-user" },
            new[] { UserId },
            out var outcome,
            out var failure);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.True);
            Assert.That(failure, Is.EqualTo(GameRecordStatsProjectionFailure.None));
            Assert.That(outcome, Is.EqualTo(new PlayerGameOutcome(PlayerRole.Robber, PlayerRole.Robber)));
        });
    }

    [Test]
    public void TryCreateOutcome_PlayerAppearsInBothRoles_ReturnsFailure()
    {
        var created = GameRecordStatsSourceProjector.TryCreateOutcome(
            UserId,
            "Police",
            new[] { UserId },
            new[] { UserId },
            out _,
            out var failure);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(failure, Is.EqualTo(GameRecordStatsProjectionFailure.PlayerInMultipleRoles));
        });
    }

    [Test]
    public void TryCreateOutcome_InvalidWinnerRole_ReturnsFailure()
    {
        var created = GameRecordStatsSourceProjector.TryCreateOutcome(
            UserId,
            "Spectator",
            new[] { UserId },
            Array.Empty<string>(),
            out _,
            out var failure);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(failure, Is.EqualTo(GameRecordStatsProjectionFailure.InvalidWinnerRole));
        });
    }
}
