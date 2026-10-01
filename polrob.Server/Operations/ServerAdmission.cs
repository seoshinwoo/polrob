namespace polrob.Server.Operations;

public sealed class ServerAdmission(ServerOperations operations, GameRecordOutbox outbox, IConfiguration configuration)
{
    public int MaxRooms { get; } = Math.Clamp(configuration.GetValue("GameNetwork:MaxRooms", 500), 1, 100_000);
    public bool CanAcceptNewGames => !operations.IsDraining && !operations.HasUnpersistedResults && outbox.CanAcceptGames;
    public bool IsReady => operations.HasStarted && CanAcceptNewGames;
}
