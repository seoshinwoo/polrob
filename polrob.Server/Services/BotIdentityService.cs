using System.Collections.Concurrent;
using polrob.Shared;

public sealed class BotIdentityService
{
    private static readonly TimeSpan IdentityLifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, BotIdentity> _identities = new();
    private readonly ILogger<BotIdentityService> _logger;

    public BotIdentityService(ILogger<BotIdentityService> logger)
    {
        _logger = logger;
    }

    public User Create(string? requestedName, PlayerRole role)
    {
        RemoveExpiredIdentities();

        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"bot-{suffix}";
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? $"Bot-{suffix[..8]}"
            : requestedName.Trim();

        var user = new User
        {
            Id = userId,
            Name = name
        };

        _identities[userId] = new BotIdentity(
            user,
            role,
            DateTimeOffset.UtcNow.Add(IdentityLifetime));

        LogCurrentCounts();
        return user;
    }

    public User? Get(string userId)
    {
        if (!_identities.TryGetValue(userId, out var identity))
        {
            return null;
        }

        if (identity.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return identity.User;
        }

        _identities.TryRemove(userId, out _);
        return null;
    }

    private void RemoveExpiredIdentities()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var identity in _identities)
        {
            if (identity.Value.ExpiresAt <= now)
            {
                _identities.TryRemove(identity.Key, out _);
            }
        }
    }

    private void LogCurrentCounts()
    {
        var identities = _identities.Values.ToArray();
        var policeCount = identities.Count(identity => identity.Role == PlayerRole.Police);
        var robberCount = identities.Count(identity => identity.Role == PlayerRole.Robber);

        _logger.LogInformation(
            "[Bot Login] Total: {TotalCount}, Police: {PoliceCount}, Robber: {RobberCount}",
            identities.Length,
            policeCount,
            robberCount);
    }

    private sealed record BotIdentity(User User, PlayerRole Role, DateTimeOffset ExpiresAt);
}
