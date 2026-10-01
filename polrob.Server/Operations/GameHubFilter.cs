using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using polrob.Server.Network;

namespace polrob.Server.Operations;

public sealed class GameHubFilter(ServerAdmission admission, IConfiguration configuration,
    OperationalMetrics metrics) : IHubFilter
{
    private readonly ConcurrentDictionary<string, UdpRateLimitState> _connections = new();
    private readonly int _maxConnections = Math.Max(1, configuration.GetValue("Limits:MaxHubConnections", 2048));
    private int _count;

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var count = Interlocked.Increment(ref _count);
        if (count > _maxConnections || !admission.CanAcceptNewGames)
        {
            Interlocked.Decrement(ref _count);
            metrics.Add("hub_connections_rejected_total");
            context.Context.Abort();
            throw new HubException("서버가 혼잡하거나 점검 중입니다.");
        }
        _connections[context.Context.ConnectionId] = new UdpRateLimitState(20);
        try { await next(context); }
        catch { Remove(context.Context.ConnectionId); throw; }
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!_connections.TryGetValue(context.Context.ConnectionId, out var limiter) ||
            !limiter.TryConsume(DateTime.UtcNow, 10, 20))
        {
            metrics.Add("hub_rate_limited_total");
            throw new HubException("요청이 너무 많습니다. 잠시 후 다시 시도해주세요.");
        }
        return await next(context);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        Remove(context.Context.ConnectionId);
        await next(context, exception);
    }

    private void Remove(string id)
    {
        if (_connections.TryRemove(id, out _)) Interlocked.Decrement(ref _count);
    }
}
