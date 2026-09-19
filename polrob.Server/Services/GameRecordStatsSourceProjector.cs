using polrob.Shared;

internal enum GameRecordStatsProjectionFailure
{
    None,
    PlayerNotFound,
    PlayerInMultipleRoles,
    InvalidWinnerRole
}

internal static class GameRecordStatsSourceProjector
{
    public static bool TryCreateOutcome(
        string userId,
        string? winnerRole,
        IReadOnlyCollection<string>? policePlayerIds,
        IReadOnlyCollection<string>? robberPlayerIds,
        out PlayerGameOutcome outcome,
        out GameRecordStatsProjectionFailure failure)
    {
        var isPolice = policePlayerIds?.Contains(userId, StringComparer.Ordinal) == true;
        var isRobber = robberPlayerIds?.Contains(userId, StringComparer.Ordinal) == true;

        if (isPolice == isRobber)
        {
            outcome = default;
            failure = isPolice
                ? GameRecordStatsProjectionFailure.PlayerInMultipleRoles
                : GameRecordStatsProjectionFailure.PlayerNotFound;
            return false;
        }

        if (!Enum.TryParse<PlayerRole>(winnerRole, ignoreCase: true, out var parsedWinnerRole) ||
            !Enum.IsDefined(parsedWinnerRole))
        {
            outcome = default;
            failure = GameRecordStatsProjectionFailure.InvalidWinnerRole;
            return false;
        }

        outcome = new PlayerGameOutcome(
            isPolice ? PlayerRole.Police : PlayerRole.Robber,
            parsedWinnerRole);
        failure = GameRecordStatsProjectionFailure.None;
        return true;
    }
}
