namespace polrob.Server.Network;

public sealed class UdpRateLimitState
{
    private readonly object _lock = new();
    private double _tokens;
    private DateTime _lastRefillUtc = DateTime.UtcNow;

    public UdpRateLimitState(double initialTokens)
    {
        _tokens = Math.Max(0d, initialTokens);
    }

    public bool TryConsume(DateTime nowUtc, double tokensPerSecond, double burstSize)
    {
        lock (_lock)
        {
            var elapsedSeconds = Math.Max(0d, (nowUtc - _lastRefillUtc).TotalSeconds);
            _lastRefillUtc = nowUtc;
            _tokens = Math.Min(burstSize, _tokens + elapsedSeconds * tokensPerSecond);

            if (_tokens < 1d)
            {
                return false;
            }

            _tokens -= 1d;
            return true;
        }
    }
}
