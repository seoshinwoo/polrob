public interface IGameRecordQueue
{
    bool TryEnqueue(CompletedGameRecord gameRecord);
}
