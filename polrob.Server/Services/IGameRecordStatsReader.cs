using polrob.Shared;

public interface IGameRecordStatsReader
{
    Task<PlayerGameStats> GetPlayerStatsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
