using polrob.Shared;

public readonly record struct PlayerGameOutcome(
    PlayerRole PlayerRole,
    PlayerRole WinnerRole);
