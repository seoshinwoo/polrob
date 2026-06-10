using System.Collections.Concurrent;

public sealed class BotIdentityService
{
    private static readonly TimeSpan IdentityLifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, BotIdentity> _identities = new();

    public User Create(string? requestedName)
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

        _identities[userId] = new BotIdentity(user, DateTimeOffset.UtcNow.Add(IdentityLifetime));
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

    private sealed record BotIdentity(User User, DateTimeOffset ExpiresAt);
}
