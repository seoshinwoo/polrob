using System.Collections.Concurrent;

public sealed class BotIdentityService
{
    private static readonly TimeSpan IdentityLifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, BotIdentity> _identities = new();

    public LoginUser Create(string? requestedName)
    {
        RemoveExpiredIdentities();

        var suffix = Guid.NewGuid().ToString("N");
        var playerId = $"bot-{suffix}";
        var displayName = string.IsNullOrWhiteSpace(requestedName)
            ? $"Bot-{suffix[..8]}"
            : requestedName.Trim();

        var user = new LoginUser
        {
            Id = playerId,
            UserId = playerId,
            LoginId = playerId,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identities[playerId] = new BotIdentity(user, DateTimeOffset.UtcNow.Add(IdentityLifetime));
        return user;
    }

    public LoginUser? Get(string playerId)
    {
        if (!_identities.TryGetValue(playerId, out var identity))
        {
            return null;
        }

        if (identity.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return identity.User;
        }

        _identities.TryRemove(playerId, out _);
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

    private sealed record BotIdentity(LoginUser User, DateTimeOffset ExpiresAt);
}
