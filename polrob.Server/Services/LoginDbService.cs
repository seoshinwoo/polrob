using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

public class LoginDbService
{
    private readonly CosmosClient _cosmosClient;
    private Database _database = null!;
    private Container _container = null!;

    private const string DatabaseId = "PolRobDB";
    private const string ContainerId = "Users";
    // 파티션 키 경로 (예: /partitionKey 또는 /userId 등)
    private const string PartitionKeyPath = "/userId";

    public LoginDbService(string connectionString)
    {
        // SDK 설정 최적화 (CamelCase 직렬화 설정 등)
        var options = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            },
            // 대기 시간을 줄이기 위해 Direct 모드 사용 권장 (기본값)
            ConnectionMode = ConnectionMode.Direct
        };

        _cosmosClient = new CosmosClient(connectionString, options);
    }

    // 앱 시작 시 최초 1회 호출하여 DB와 컨테이너가 없다면 자동 생성
    public async Task InitializeAsync()
    {
        _database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
        _container = await _database.CreateContainerIfNotExistsAsync(ContainerId, PartitionKeyPath);
    }

    // 1. Create / Update (Upsert)
    public async Task UpsertItemAsync<T>(T item, string partitionKey) where T : class
    {
        // Cosmos DB는 ID 기반 덮어쓰기(Upsert)가 매우 유용합니다.
        await _container.UpsertItemAsync(item, new PartitionKey(partitionKey));
    }

    // 2. Read (단일 항목 조회)
    public async Task<T?> GetItemAsync<T>(string id, string partitionKey) where T : class
    {
        try
        {
            ItemResponse<T> response = await _container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // 데이터가 없는 경우 예외 처리
        }
    }

    // 3. Query (SQL 스타일 조건 조회)
    public async Task<List<T>> GetItemsByQueryAsync<T>(string queryString) where T : class
    {
        var query = _container.GetItemQueryIterator<T>(new QueryDefinition(queryString));
        var results = new List<T>();

        while (query.HasMoreResults)
        {
            var response = await query.ReadNextAsync();
            results.AddRange(response.Resource);
        }

        return results;
    }

    // 4. Delete
    public async Task DeleteItemAsync(string id, string partitionKey)
    {
        await _container.DeleteItemAsync<object>(id, new PartitionKey(partitionKey));
    }

    public async Task<LoginUser?> CreateUserAsync(string loginId, string displayName, string password)
    {
        var normalizedLoginId = NormalizeLoginId(loginId);
        var safeDisplayName = displayName.Trim();

        var user = new LoginUser
        {
            Id = normalizedLoginId,
            UserId = normalizedLoginId,
            LoginId = normalizedLoginId,
            DisplayName = safeDisplayName,
            PasswordHash = HashPassword(password),
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _container.CreateItemAsync(user, new PartitionKey(user.UserId));
            return user;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    public async Task<LoginUser?> ValidateUserAsync(string loginId, string password)
    {
        var user = await GetUserByLoginIdAsync(NormalizeLoginId(loginId));
        if (user is null || !VerifyPassword(password, user.PasswordHash))
        {
            return null;
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _container.ReplaceItemAsync(user, user.Id, new PartitionKey(user.UserId));
        return user;
    }

    private async Task<LoginUser?> GetUserByLoginIdAsync(string normalizedLoginId)
    {
        try
        {
            var response = await _container.ReadItemAsync<LoginUser>(
                normalizedLoginId,
                new PartitionKey(normalizedLoginId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static string NormalizeLoginId(string loginId)
    {
        return loginId.Trim().ToLowerInvariant();
    }

    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return $"PBKDF2-SHA256:{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256")
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

public sealed class LoginUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("loginId")]
    public string LoginId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public DateTimeOffset? LastLoginAt { get; set; }
}
