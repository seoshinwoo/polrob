using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using polrob.Shared;

public sealed class GameRecordDbService
{
    private const string GameRecordPartitionKeyPath = "/id";
    private const string PlayerGameRecordPartitionKeyPath = "/playerId";

    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<GameRecordDbService> _logger;
    private readonly string _databaseId;
    private readonly string _gameRecordsContainerId;
    private readonly string _playerGameRecordsContainerId;
    private Container _gameRecordsContainer = null!;
    private Container _playerGameRecordsContainer = null!;

    public GameRecordDbService(
        CosmosClient cosmosClient,
        IOptions<CosmosDbOptions> options,
        ILogger<GameRecordDbService> logger)
    {
        _cosmosClient = cosmosClient;
        _logger = logger;

        var cosmosOptions = options.Value;
        _databaseId = GetConfiguredName(cosmosOptions.DatabaseId, "PolRobDB");
        _gameRecordsContainerId = GetConfiguredName(
            cosmosOptions.GameRecordsContainerId,
            "GameRecords");
        _playerGameRecordsContainerId = GetConfiguredName(
            cosmosOptions.PlayerGameRecordsContainerId,
            "PlayerGameRecords");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(
            _databaseId,
            cancellationToken: cancellationToken);
        var gameRecordsContainerTask = database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_gameRecordsContainerId, GameRecordPartitionKeyPath),
            cancellationToken: cancellationToken);
        var playerGameRecordsContainerTask = database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(
                _playerGameRecordsContainerId,
                PlayerGameRecordPartitionKeyPath),
            cancellationToken: cancellationToken);

        await Task.WhenAll(gameRecordsContainerTask, playerGameRecordsContainerTask);
        _gameRecordsContainer = gameRecordsContainerTask.Result.Container;
        _playerGameRecordsContainer = playerGameRecordsContainerTask.Result.Container;
    }

    public async Task SaveGameRecordAsync(
        CompletedGameRecord gameRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameRecord);
        ValidateIdentifier(gameRecord.Id, nameof(gameRecord.Id));
        ValidateIdentifier(gameRecord.RoomId, nameof(gameRecord.RoomId));
        if (!Enum.IsDefined(gameRecord.WinnerRole))
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameRecord),
                "The winning role is invalid.");
        }

        var policePlayerIds = NormalizePlayerIds(gameRecord.PolicePlayerIds);
        var robberPlayerIds = NormalizePlayerIds(gameRecord.RobberPlayerIds);
        if (policePlayerIds.Intersect(robberPlayerIds, StringComparer.Ordinal).Any())
        {
            throw new ArgumentException(
                "A player cannot be recorded as both Police and Robber in the same game.",
                nameof(gameRecord));
        }

        if (gameRecord.DurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameRecord),
                "Game duration cannot be negative.");
        }

        var startedAtUtc = NormalizeUtc(gameRecord.StartedAtUtc);
        var endedAtUtc = NormalizeUtc(gameRecord.EndedAtUtc);
        if (endedAtUtc < startedAtUtc)
        {
            throw new ArgumentException(
                "The game end time cannot be earlier than its start time.",
                nameof(gameRecord));
        }

        var document = new GameRecordDocument
        {
            Id = gameRecord.Id,
            RoomId = gameRecord.RoomId,
            WinnerRole = gameRecord.WinnerRole.ToString(),
            PolicePlayerIds = policePlayerIds,
            RobberPlayerIds = robberPlayerIds,
            DurationSeconds = gameRecord.DurationSeconds,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            SchemaVersion = 1
        };

        try
        {
            await _gameRecordsContainer.CreateItemAsync(
                document,
                new PartitionKey(document.Id),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogDebug(
                "Game record {GameRecordId} already exists; treating the write as idempotently completed.",
                document.Id);
        }

        var playerDocuments = policePlayerIds
            .Select(playerId => CreatePlayerDocument(document, playerId, PlayerRole.Police))
            .Concat(robberPlayerIds.Select(
                playerId => CreatePlayerDocument(document, playerId, PlayerRole.Robber)));

        await Task.WhenAll(playerDocuments.Select(
            playerDocument => SavePlayerGameRecordAsync(playerDocument, cancellationToken)));
    }

    public async Task<PlayerGameStats> GetPlayerStatsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(userId, nameof(userId));

        var query = new QueryDefinition(
                "SELECT c.playerRole, c.winnerRole " +
                "FROM c WHERE c.playerId = @userId")
            .WithParameter("@userId", userId);

        var requestOptions = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        };
        var accumulator = new GameRecordStatsAccumulator();
        using var iterator = _playerGameRecordsContainer
            .GetItemQueryIterator<PlayerGameRecordStatsProjection>(
                query,
                requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var document in response)
            {
                if (!TryParsePlayerRole(document.PlayerRole, out var playerRole) ||
                    !TryParsePlayerRole(document.WinnerRole, out var winnerRole))
                {
                    _logger.LogWarning(
                        "Ignoring a player game record with invalid roles {PlayerRole}/{WinnerRole} while calculating stats for user {UserId}.",
                        document.PlayerRole,
                        document.WinnerRole,
                        userId);
                    continue;
                }

                accumulator.Add(new PlayerGameOutcome(playerRole, winnerRole));
            }
        }

        return accumulator.Build();
    }

    private async Task SavePlayerGameRecordAsync(
        PlayerGameRecordDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            await _playerGameRecordsContainer.CreateItemAsync(
                document,
                new PartitionKey(document.PlayerId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogDebug(
                "Player game record {GameRecordId}/{PlayerId} already exists; treating the write as idempotently completed.",
                document.Id,
                document.PlayerId);
        }
    }

    private static PlayerGameRecordDocument CreatePlayerDocument(
        GameRecordDocument gameRecord,
        string playerId,
        PlayerRole playerRole) => new()
    {
        Id = gameRecord.Id,
        PlayerId = playerId,
        RoomId = gameRecord.RoomId,
        PlayerRole = playerRole.ToString(),
        WinnerRole = gameRecord.WinnerRole,
        DurationSeconds = gameRecord.DurationSeconds,
        StartedAtUtc = gameRecord.StartedAtUtc,
        EndedAtUtc = gameRecord.EndedAtUtc,
        SchemaVersion = gameRecord.SchemaVersion
    };

    private static bool TryParsePlayerRole(string? value, out PlayerRole role)
    {
        return Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);
    }

    private static string[] NormalizePlayerIds(IReadOnlyList<string> playerIds)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        return playerIds
            .Where(playerId => !string.IsNullOrWhiteSpace(playerId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(playerId => playerId, StringComparer.Ordinal)
            .ToArray();
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static string GetConfiguredName(string? configuredName, string defaultName)
    {
        return string.IsNullOrWhiteSpace(configuredName) ? defaultName : configuredName;
    }

    private sealed class GameRecordDocument
    {
        public string Id { get; init; } = string.Empty;
        public string RoomId { get; init; } = string.Empty;
        public string WinnerRole { get; init; } = string.Empty;
        public string[] PolicePlayerIds { get; init; } = Array.Empty<string>();
        public string[] RobberPlayerIds { get; init; } = Array.Empty<string>();
        public int DurationSeconds { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public DateTime EndedAtUtc { get; init; }
        public int SchemaVersion { get; init; }
    }

    private sealed class PlayerGameRecordDocument
    {
        public string Id { get; init; } = string.Empty;
        public string PlayerId { get; init; } = string.Empty;
        public string RoomId { get; init; } = string.Empty;
        public string PlayerRole { get; init; } = string.Empty;
        public string WinnerRole { get; init; } = string.Empty;
        public int DurationSeconds { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public DateTime EndedAtUtc { get; init; }
        public int SchemaVersion { get; init; }
    }

    private sealed class PlayerGameRecordStatsProjection
    {
        public string? PlayerRole { get; init; }
        public string? WinnerRole { get; init; }
    }
}
