using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

public class UserDbService
{
    private readonly CosmosClient _cosmosClient;
    private readonly ConcurrentDictionary<string, User> _usersById = new();
    private Database _database = null!;
    private Container _container = null!;

    private const string DatabaseId = "PolRobDB";
    private const string ContainerId = "Users";
    private const string PartitionKeyPath = "/id";

    public UserDbService(string connectionString)
    {
        var options = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            },
            ConnectionMode = ConnectionMode.Direct
        };

        _cosmosClient = new CosmosClient(connectionString, options);
    }

    public async Task InitializeAsync()
    {
        _database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseId);
        var containerProperties = new ContainerProperties(ContainerId, PartitionKeyPath);
        containerProperties.UniqueKeyPolicy.UniqueKeys.Add(new UniqueKey
        {
            Paths = { "/name" }
        });
        _container = await _database.CreateContainerIfNotExistsAsync(containerProperties);
    }

    public async Task<User?> CreateUserAsync(string name, string password)
    {
        var normalizedName = NormalizeName(name);
        if (await GetDocumentByNameAsync(normalizedName) is not null)
        {
            return null;
        }

        var document = new UserDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = normalizedName,
            PasswordHash = HashPassword(password)
        };

        try
        {
            await _container.CreateItemAsync(document, new PartitionKey(document.Id));
            var user = document.ToUser();
            _usersById[user.Id] = user;
            return user;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    public async Task<User?> ValidateUserAsync(string name, string password)
    {
        var document = await GetDocumentByNameAsync(NormalizeName(name));
        if (document is null || !VerifyPassword(password, document.PasswordHash))
        {
            return null;
        }

        var user = document.ToUser();
        _usersById[user.Id] = user;
        return user;
    }

    public async Task<User?> GetUserAsync(string id)
    {
        if (_usersById.TryGetValue(id, out var cachedUser))
        {
            return cachedUser;
        }

        try
        {
            var response = await _container.ReadItemAsync<UserDocument>(id, new PartitionKey(id));
            var user = response.Resource.ToUser();
            _usersById[user.Id] = user;
            return user;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<UserDocument?> GetDocumentByNameAsync(string normalizedName)
    {
        var query = new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.name = @name")
            .WithParameter("@name", normalizedName);
        using var iterator = _container.GetItemQueryIterator<UserDocument>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            var document = response.Resource.FirstOrDefault();
            if (document is not null)
            {
                return document;
            }
        }

        return null;
    }

    private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

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

    private sealed class UserDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("passwordHash")]
        public string PasswordHash { get; set; } = string.Empty;

        public User ToUser() => new() { Id = Id, Name = Name };
    }
}

public sealed class User
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
