public sealed class GameRecordReconciler : BackgroundService
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(5);

    private readonly GameRecordDbService _gameRecordDbService;
    private readonly ILogger<GameRecordReconciler> _logger;

    public GameRecordReconciler(
        GameRecordDbService gameRecordDbService,
        ILogger<GameRecordReconciler> logger)
    {
        _gameRecordDbService = gameRecordDbService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _gameRecordDbService.RepairIncompletePlayerIndexesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Game record index reconciliation failed.");
            }

            try
            {
                await Task.Delay(ReconciliationInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
