public interface IGameRecordQueue
{
    // Production implementation returns true only after flushing a local durable record.
    // false must retain the result and be retried; it is never a successful completion.
    bool TryEnqueue(CompletedGameRecord gameRecord);
}
