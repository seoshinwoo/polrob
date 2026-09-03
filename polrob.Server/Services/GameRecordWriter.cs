using System.Net;
using System.Threading.Channels;
using Microsoft.Azure.Cosmos;

public sealed class GameRecordWriter : BackgroundService, IGameRecordQueue
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly Channel<CompletedGameRecord> _records = Channel.CreateUnbounded<CompletedGameRecord>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private readonly GameRecordDbService _gameRecordDbService;
    private readonly ILogger<GameRecordWriter> _logger;
    private readonly CancellationTokenSource _abortWrites = new();

    public GameRecordWriter(
        GameRecordDbService gameRecordDbService,
        ILogger<GameRecordWriter> logger)
    {
        _gameRecordDbService = gameRecordDbService;
        _logger = logger;
    }

    public bool TryEnqueue(CompletedGameRecord gameRecord)
    {
        ArgumentNullException.ThrowIfNull(gameRecord);

        var snapshot = gameRecord with
        {
            PolicePlayerIds = gameRecord.PolicePlayerIds.ToArray(),
            RobberPlayerIds = gameRecord.RobberPlayerIds.ToArray()
        };

        return _records.Writer.TryWrite(snapshot);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stopRegistration = stoppingToken.Register(
            () => _records.Writer.TryComplete());

        try
        {
            // On a normal shutdown, finish records already accepted by the channel.
            // This separate token is cancelled only if the shutdown grace period expires.
            await foreach (var gameRecord in _records.Reader.ReadAllAsync(_abortWrites.Token))
            {
                await WriteWithRetryAsync(gameRecord, _abortWrites.Token);
            }
        }
        catch (OperationCanceledException) when (_abortWrites.IsCancellationRequested)
        {
            // The host shutdown grace period expired.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _records.Writer.TryComplete();

        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _abortWrites.Cancel();
            }
        }
    }

    public override void Dispose()
    {
        _records.Writer.TryComplete();
        _abortWrites.Cancel();
        _abortWrites.Dispose();
        base.Dispose();
    }

    private async Task WriteWithRetryAsync(
        CompletedGameRecord gameRecord,
        CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _gameRecordDbService.SaveGameRecordAsync(gameRecord, stoppingToken);
                _logger.LogInformation(
                    "Saved completed game record {GameRecordId} for room {RoomId}.",
                    gameRecord.Id,
                    gameRecord.RoomId);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                var exponentialStep = Math.Min(attempt - 1, 7);
                var retryDelay = TimeSpan.FromMilliseconds(Math.Min(
                    MaxRetryDelay.TotalMilliseconds,
                    InitialRetryDelay.TotalMilliseconds * Math.Pow(2, exponentialStep)));

                _logger.LogWarning(
                    ex,
                    "Transient failure saving game record {GameRecordId}. Retrying in {RetryDelayMilliseconds} ms (attempt {Attempt}).",
                    gameRecord.Id,
                    retryDelay.TotalMilliseconds,
                    attempt);

                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "A permanent failure prevented saving game record {GameRecordId} on attempt {AttemptCount}.",
                    gameRecord.Id,
                    attempt);
                return;
            }
        }
    }

    private static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            CosmosException cosmosException => cosmosException.StatusCode is
                HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout,
            HttpRequestException => true,
            TimeoutException => true,
            OperationCanceledException => true,
            _ => false
        };
    }
}
