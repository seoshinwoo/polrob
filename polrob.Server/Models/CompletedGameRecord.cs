using polrob.Shared;

public sealed record CompletedGameRecord(
    string Id,
    string RoomId,
    PlayerRole WinnerRole,
    IReadOnlyList<string> PolicePlayerIds,
    IReadOnlyList<string> RobberPlayerIds,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    int DurationSeconds);
