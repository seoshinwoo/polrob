using System.Numerics;
using polrob.Shared;

namespace polrob.Test;

public sealed class BotMovementController
{
    private const float MovementUnitsPerSecondMultiplier = 60f;
    private const float RescueArrivalDistance = 20f;
    private static readonly float[] SteeringAngles = [0f, 25f, -25f, 50f, -50f, 90f, -90f, 135f, -135f, 180f];

    private readonly GameMap _map = new();
    private readonly Random _random;
    private Vector2 _wanderDirection;
    private DateTime _nextDirectionChangeUtc = DateTime.MinValue;
    private DateTime _pausedUntilUtc = DateTime.MinValue;
    private DateTime _nextPauseCheckUtc = DateTime.MinValue;

    public BotMovementController(string botId)
    {
        _random = new Random(StringComparer.Ordinal.GetHashCode(botId));
        _wanderDirection = CreateRandomDirection();
    }

    public bool Update(
        Player localPlayer,
        IReadOnlyCollection<Player> visibleTeamPlayers,
        TimeSpan elapsed)
    {
        if (localPlayer.Role == PlayerRole.Robber && IsInJail(localPlayer))
        {
            localPlayer.IsMoving = false;
            return false;
        }

        if (TryGetRescueTarget(localPlayer, visibleTeamPlayers, out var rescueTarget))
        {
            var toTarget = rescueTarget - new Vector2(localPlayer.X, localPlayer.Y);
            if (toTarget.LengthSquared() <= RescueArrivalDistance * RescueArrivalDistance)
            {
                localPlayer.IsMoving = false;
                return false;
            }

            _pausedUntilUtc = DateTime.MinValue;
            return Move(localPlayer, Vector2.Normalize(toTarget), elapsed);
        }

        var now = DateTime.UtcNow;
        if (now < _pausedUntilUtc)
        {
            localPlayer.IsMoving = false;
            return false;
        }

        if (now >= _nextPauseCheckUtc && ShouldPause(localPlayer.Role))
        {
            _pausedUntilUtc = now.Add(GetPauseDuration(localPlayer.Role));
            _nextPauseCheckUtc = _pausedUntilUtc.AddMilliseconds(_random.Next(1200, 3200));
            localPlayer.IsMoving = false;
            return false;
        }

        if (now >= _nextPauseCheckUtc)
        {
            _nextPauseCheckUtc = now.AddMilliseconds(_random.Next(1200, 3200));
        }

        if (now >= _nextDirectionChangeUtc)
        {
            _wanderDirection = CreateRandomDirection();
            _nextDirectionChangeUtc = now.AddMilliseconds(
                _random.Next(900, 2800));
        }

        if (Move(localPlayer, _wanderDirection, elapsed))
        {
            return true;
        }

        _wanderDirection = CreateRandomDirection();
        _nextDirectionChangeUtc = now.AddMilliseconds(
            _random.Next(500, 1400));
        return Move(localPlayer, _wanderDirection, elapsed);
    }

    private bool ShouldPause(PlayerRole role)
    {
        var chance = role == PlayerRole.Robber ? 0.22d : 0.08d;
        return _random.NextDouble() < chance;
    }

    private TimeSpan GetPauseDuration(PlayerRole role)
    {
        var milliseconds = role == PlayerRole.Robber
            ? _random.Next(800, 2400)
            : _random.Next(400, 1200);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private bool TryGetRescueTarget(
        Player localPlayer,
        IReadOnlyCollection<Player> visibleTeamPlayers,
        out Vector2 target)
    {
        target = default;
        if (localPlayer.Role != PlayerRole.Robber)
        {
            return false;
        }

        var jailedRobberCount = visibleTeamPlayers.Count(
            player => player.Role == PlayerRole.Robber && IsInJail(player));
        if (jailedRobberCount == 0)
        {
            return false;
        }

        var freeRobberIds = visibleTeamPlayers
            .Where(player => player.Role == PlayerRole.Robber && !IsInJail(player))
            .Select(player => player.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var rescuerIndex = freeRobberIds.IndexOf(localPlayer.Id);
        if (rescuerIndex < 0 || rescuerIndex >= jailedRobberCount)
        {
            return false;
        }

        target = GetRescueContactPoint(localPlayer.Radius, rescuerIndex);
        return true;
    }

    private Vector2 GetRescueContactPoint(float radius, int rescuerIndex)
    {
        var jail = _map.Jail;
        var contactGap = radius + 15f;
        var candidates = new[]
        {
            new Vector2(jail.LeftTop.X - contactGap, jail.Center.Y),
            new Vector2(jail.RightBottom.X + contactGap, jail.Center.Y),
            new Vector2(jail.LeftTop.X + jail.Width * 0.2f, jail.RightBottom.Y + contactGap),
            new Vector2(jail.LeftTop.X + jail.Width * 0.8f, jail.RightBottom.Y + contactGap),
            new Vector2(jail.Center.X, jail.LeftTop.Y - contactGap)
        };

        for (var offset = 0; offset < candidates.Length; offset++)
        {
            var candidate = candidates[(rescuerIndex + offset) % candidates.Length];
            if (!IsColliding(candidate.X, candidate.Y, radius))
            {
                return candidate;
            }
        }

        return new Vector2(jail.LeftTop.X - contactGap, jail.Center.Y);
    }

    private bool Move(Player player, Vector2 preferredDirection, TimeSpan elapsed)
    {
        if (preferredDirection.LengthSquared() < 0.001f)
        {
            player.IsMoving = false;
            return false;
        }

        var distance = player.Speed
            * MovementUnitsPerSecondMultiplier
            * Math.Clamp((float)elapsed.TotalSeconds, 0f, 0.1f);

        foreach (var angle in SteeringAngles)
        {
            var direction = Rotate(preferredDirection, angle);
            var nextX = Math.Clamp(
                player.X + direction.X * distance,
                player.Radius,
                _map.Width - player.Radius);
            var nextY = Math.Clamp(
                player.Y + direction.Y * distance,
                player.Radius,
                _map.Height - player.Radius);

            var moved = false;
            if (!IsColliding(nextX, player.Y, player.Radius))
            {
                player.X = nextX;
                moved = true;
            }

            if (!IsColliding(player.X, nextY, player.Radius))
            {
                player.Y = nextY;
                moved = true;
            }

            if (!moved)
            {
                continue;
            }

            player.Angle = (float)(Math.Atan2(direction.Y, direction.X) * 180f / Math.PI) - 90f;
            player.IsMoving = true;
            _wanderDirection = direction;
            return true;
        }

        player.IsMoving = false;
        return false;
    }

    private bool IsColliding(float x, float y, float radius)
    {
        if (IsCircleCollidingWithRectangle(x, y, radius, _map.Jail))
        {
            return true;
        }

        foreach (var obstacle in _map.Obstacles)
        {
            if (obstacle.Type == "Rect")
            {
                var closestX = Math.Clamp(x, obstacle.LeftTop.X, obstacle.RightBottom.X);
                var closestY = Math.Clamp(y, obstacle.LeftTop.Y, obstacle.RightBottom.Y);
                var dx = x - closestX;
                var dy = y - closestY;
                if (dx * dx + dy * dy < radius * radius)
                {
                    return true;
                }
            }
            else if (obstacle.Type == "Circle")
            {
                var dx = x - obstacle.CenterX.X;
                var dy = y - obstacle.CenterX.Y;
                var combinedRadius = radius + obstacle.Radius;
                if (dx * dx + dy * dy < combinedRadius * combinedRadius)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsInJail(Player player)
    {
        return player.X >= _map.Jail.LeftTop.X &&
               player.X <= _map.Jail.RightBottom.X &&
               player.Y >= _map.Jail.LeftTop.Y &&
               player.Y <= _map.Jail.RightBottom.Y;
    }

    private static bool IsCircleCollidingWithRectangle(
        float x,
        float y,
        float radius,
        MapBuilding building)
    {
        var closestX = Math.Clamp(x, building.LeftTop.X, building.RightBottom.X);
        var closestY = Math.Clamp(y, building.LeftTop.Y, building.RightBottom.Y);
        var dx = x - closestX;
        var dy = y - closestY;
        return dx * dx + dy * dy < radius * radius;
    }

    private Vector2 CreateRandomDirection()
    {
        var angle = _random.NextDouble() * Math.PI * 2d;
        return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
    }

    private static Vector2 Rotate(Vector2 direction, float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return Vector2.Normalize(new Vector2(
            direction.X * cos - direction.Y * sin,
            direction.X * sin + direction.Y * cos));
    }
}
