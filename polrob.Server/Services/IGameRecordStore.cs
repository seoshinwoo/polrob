public interface IGameRecordStore
{
    Task SaveGameRecordAsync(CompletedGameRecord record, CancellationToken cancellationToken = default);
}
