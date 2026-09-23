using System.Drawing;
using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class TownMapPhysicsTests
{
    [Test]
    public void ActiveMapRegistersAssetProfilesAndNonSolidHidingAreas()
    {
        var map = new GameMap();
        Assert.Multiple(() =>
        {
            Assert.That(map.Width, Is.EqualTo(2560f));
            Assert.That(map.Height, Is.EqualTo(3840f));
            Assert.That(map.PropLayouts, Is.SameAs(ChaseTownLayout.Props));
            Assert.That(map.PropLayouts.Length, Is.EqualTo(89));
            Assert.That(map.PropLayouts.All(prop => prop.AssetPath.StartsWith("ChaseTownV7/", StringComparison.Ordinal)), Is.True);
            Assert.That(map.Buildings.Count, Is.EqualTo(map.PropLayouts.Count(prop => prop.BuildingType != null)));
            Assert.That(map.PoliceStation.BlocksMovement, Is.True);
            Assert.That(map.Jail.BlocksMovement, Is.False, "Only its bars and gate are solid, not the whole jail.");
            Assert.That(map.PoliceStation.ImageFileName, Is.Not.Empty);
            Assert.That(map.Jail.ImageFileName, Is.Not.Empty);
            Assert.That(map.Obstacles.Count(o => o.IsHidingArea), Is.GreaterThan(20));
        });

        foreach (var building in map.Buildings)
        {
            var layout = map.PropLayouts.Single(prop => prop.BuildingType == building.Type);
            Assert.Multiple(() =>
            {
                Assert.That(building.Center.X, Is.EqualTo(layout.CenterX));
                Assert.That(building.Center.Y, Is.EqualTo(layout.CenterY));
                Assert.That(building.CollisionPolygon.Length, building.Type == "Jail" ? Is.EqualTo(0) : Is.GreaterThan(3));
                Assert.That(building.BlocksVision, Is.EqualTo(building.Type != "Jail"), building.Type);
                Assert.That(building.BlocksMovement, Is.EqualTo(building.Type != "Jail"), building.Type);
            });
        }
    }

    [TestCase(25f)]
    [TestCase(50f)]
    public void SpecifiedColliderCentersAndWorldEdgesBlockMovement(float playerRadius)
    {
        var map = new GameMap();
        var nearby = new List<Obstacle>();
        foreach (var building in map.Buildings.Where(b => b.BlocksMovement))
        {
            Assert.That(map.IsMovementPositionBlocked(building.CollisionCenter.X, building.CollisionCenter.Y, playerRadius, nearby),
                Is.True, building.Type);
        }
        foreach (var obstacle in map.Obstacles.Where(o => o.BlocksMovement))
        {
            Assert.That(map.IsMovementPositionBlocked(obstacle.Center.X, obstacle.Center.Y, playerRadius, nearby),
                Is.True, obstacle.ImageFileName);
        }
        Assert.Multiple(() =>
        {
            Assert.That(map.IsMovementPositionBlocked(playerRadius - 1f, 1920f, playerRadius, nearby), Is.True);
            Assert.That(map.IsMovementPositionBlocked(map.Width - playerRadius + 1f, 1920f, playerRadius, nearby), Is.True);
            Assert.That(map.IsMovementPositionBlocked(1280f, playerRadius - 1f, playerRadius, nearby), Is.True);
            Assert.That(map.IsMovementPositionBlocked(1280f, map.Height - playerRadius + 1f, playerRadius, nearby), Is.True);
        });
    }

    [TestCase(25f)]
    [TestCase(50f)]
    public void CircularPropsHaveRoundClearanceInsteadOfInvisibleSquareCorners(float playerRadius)
    {
        var obstacle = new Obstacle { Type = "Circle", CenterX = new PointF(500f, 500f), Radius = 70f };
        var combinedRadius = obstacle.Radius + playerRadius;
        Assert.Multiple(() =>
        {
            Assert.That(GameMap.IsCircleCollidingWithObstacle(500f + combinedRadius - 0.1f, 500f, playerRadius, obstacle), Is.True);
            Assert.That(GameMap.IsCircleCollidingWithObstacle(500f + combinedRadius + 0.1f, 500f, playerRadius, obstacle), Is.False);
            Assert.That(GameMap.IsCircleCollidingWithObstacle(500f + combinedRadius * 0.8f, 500f + combinedRadius * 0.8f, playerRadius, obstacle), Is.False);
        });
    }

    [TestCase(25f)]
    [TestCase(50f)]
    public void RoleSpawnsAreDistinctWalkableAndStable(float playerRadius)
    {
        var map = new GameMap();
        var nearby = new List<Obstacle>();
        foreach (var role in new[] { PlayerRole.Police, PlayerRole.Robber })
        {
            var spawns = Enumerable.Range(0, 16).Select(slot => map.GetSpawnPosition(role, slot, playerRadius)).ToArray();
            Assert.That(spawns.Distinct().Count(), Is.EqualTo(spawns.Length), role.ToString());
            for (var index = 0; index < spawns.Length; index++)
            {
                var spawn = spawns[index];
                Assert.That(map.IsMovementPositionBlocked(spawn.X, spawn.Y, playerRadius, nearby), Is.False, $"{role} slot {index}");
                Assert.That(map.GetSpawnPosition(role, index, playerRadius), Is.EqualTo(spawn));
            }
        }
    }

    [TestCase(25f)]
    [TestCase(50f)]
    public void JailFrontSupportsRescueReleaseAndTravelFromBothRoleSpawns(float playerRadius)
    {
        var map = new GameMap();
        var nearby = new List<Obstacle>();
        var jailBounds = GameMap.GetBuildingCollisionBounds(map.Jail);
        var release = new PointF(map.Jail.CollisionCenter.X, jailBounds.Bottom + playerRadius + 20f);
        Assert.That(map.IsMovementPositionBlocked(release.X, release.Y, playerRadius, nearby), Is.False, "Jail release apron is blocked.");
        Assert.That(GameMap.GetDistanceSquaredToBuilding(release.X, release.Y, map.Jail),
            Is.LessThanOrEqualTo(MathF.Pow(playerRadius + 90f, 2f)), "Release apron must also be reachable for rescue contact.");

        var reachable = FloodWalkableGrid(map, map.GetSpawnPosition(PlayerRole.Police, 0, playerRadius), playerRadius);
        AssertPointReachable(map, reachable, map.GetSpawnPosition(PlayerRole.Robber, 0, playerRadius), playerRadius, "Robber spawn");
        AssertPointReachable(map, reachable, release, playerRadius, "Jail release apron");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void JailHoldingSlotsAreCenteredInsideTheArtworkAndDoNotOverlap(int playerCount)
    {
        const float playerRadius = 25f;
        var map = new GameMap();
        var positions = Enumerable.Range(0, playerCount)
            .Select(slot => map.GetJailHoldingPosition(slot, playerCount, playerRadius))
            .ToArray();
        var holding = GameMap.GetObstacleBounds(map.JailHoldingArea!);

        Assert.Multiple(() =>
        {
            Assert.That(positions.Average(position => position.X), Is.EqualTo(map.Jail.Center.X).Within(0.001f));
            Assert.That(positions.All(position => position.X - playerRadius >= holding.Left), Is.True);
            Assert.That(positions.All(position => position.X + playerRadius <= holding.Right), Is.True);
            Assert.That(positions.All(position => position.Y - playerRadius >= holding.Top && position.Y + playerRadius <= holding.Bottom), Is.True);
            Assert.That(positions.All(position => !map.IsMovementPositionBlocked(position.X, position.Y, playerRadius, [])), Is.True);
        });

        for (var first = 0; first < positions.Length; first++)
        {
            for (var second = first + 1; second < positions.Length; second++)
            {
                var deltaX = positions[first].X - positions[second].X;
                var deltaY = positions[first].Y - positions[second].Y;
                Assert.That(
                    deltaX * deltaX + deltaY * deltaY,
                    Is.GreaterThanOrEqualTo(MathF.Pow(playerRadius * 2f, 2f)));
            }
        }
    }

    private const float GridStep = 16f;

    [Test]
    public void GroundIsThreeReusableTilesAndTheRoadNetworkIsConnected()
    {
        Assert.That(ChaseTownLayout.Columns * ChaseTownLayout.TileSize, Is.EqualTo(GameMap.WorldWidth));
        Assert.That(ChaseTownLayout.Rows * ChaseTownLayout.TileSize, Is.EqualTo(GameMap.WorldHeight));
        var roads = new HashSet<(int X, int Y)>();
        var terrain = new HashSet<GroundTile>();
        for (var y = 0; y < ChaseTownLayout.Rows; y++)
        for (var x = 0; x < ChaseTownLayout.Columns; x++)
        {
            terrain.Add(ChaseTownLayout.Ground[x,y]);
            if (ChaseTownLayout.Ground[x,y] == GroundTile.Road) roads.Add((x,y));
        }
        Assert.That(terrain.Count, Is.EqualTo(3));
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(roads.First());
        var seen = new HashSet<(int X, int Y)>();
        while (queue.TryDequeue(out var p))
        {
            if (!roads.Contains(p) || !seen.Add(p)) continue;
            queue.Enqueue((p.X-1,p.Y)); queue.Enqueue((p.X+1,p.Y));
            queue.Enqueue((p.X,p.Y-1)); queue.Enqueue((p.X,p.Y+1));
        }
        Assert.That(seen.SetEquals(roads), Is.True);
    }

    [Test]
    public void EveryHidingAreaCanBeEnteredFromTheTown()
    {
        var map = new GameMap();
        var reachable = FloodWalkableGrid(map, map.GetSpawnPosition(PlayerRole.Robber, 0, 25), 25);
        foreach (var bush in map.Obstacles.Where(o => o.IsHidingArea))
        {
            Assert.That(bush.BlocksMovement || bush.BlocksVision, Is.False);
            Assert.That(map.FindBushContainingPoint(bush.Center.X, bush.Center.Y), Is.Not.Null);
            Assert.That(map.IsMovementPositionBlocked(bush.Center.X, bush.Center.Y, 25, []), Is.False,
                $"Hidden inside a solid: {bush.Center}");
            AssertPointReachable(map, reachable, bush.Center, 25, "Hiding area");
        }
    }

    [Test]
    public void RescueTriggerIsOutsideTheBarsAndReachable()
    {
        var map = new GameMap();
        var center = map.JailRescueArea!.Center;
        Assert.That(GameMap.ContainsPoint(map.JailRescueArea, center.X, center.Y), Is.True);
        Assert.That(map.IsMovementPositionBlocked(center.X, center.Y, 25, []), Is.False);
        var reachable = FloodWalkableGrid(map, map.GetSpawnPosition(PlayerRole.Robber, 0, 25), 25);
        AssertPointReachable(map, reachable, center, 25, "Rescue trigger");
    }

    [TestCase(25f)]
    [TestCase(50f)]
    public void ReferenceMapLandmarksJoinThePlayableTown(float playerRadius)
    {
        var map = new GameMap();
        var reachable = FloodWalkableGrid(map, map.GetSpawnPosition(PlayerRole.Police, 0, playerRadius), playerRadius);
        var nearby = new List<Obstacle>();
        foreach (var point in ChaseTownLayout.ChaseWaypoints)
        {
            Assert.That(map.IsMovementPositionBlocked(point.X, point.Y, playerRadius, nearby),
                Is.False, $"Chase route is pinched shut at {point}.");
            AssertPointReachable(map, reachable, point, playerRadius, $"Chase route {point}");
        }
    }

    private static HashSet<(int X, int Y)> FloodWalkableGrid(GameMap map, PointF start, float radius)
    {
        var nearby = new List<Obstacle>();
        var startCell = FindConnectedGridCell(map, start, radius);
        var visited = new HashSet<(int X, int Y)> { startCell };
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue(startCell);
        (int X, int Y)[] directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (pending.TryDequeue(out var current))
        {
            foreach (var direction in directions)
            {
                var next = (X: current.X + direction.X, Y: current.Y + direction.Y);
                if (next.X < 0 || next.Y < 0 || next.X * GridStep > map.Width || next.Y * GridStep > map.Height || visited.Contains(next)) continue;
                if (map.IsMovementPositionBlocked(next.X * GridStep, next.Y * GridStep, radius, nearby)) continue;
                if (map.IsMovementPositionBlocked((next.X + current.X) * GridStep / 2f, (next.Y + current.Y) * GridStep / 2f, radius, nearby)) continue;
                visited.Add(next);
                pending.Enqueue(next);
            }
        }
        return visited;
    }

    private static void AssertPointReachable(GameMap map, HashSet<(int X, int Y)> reachable, PointF point, float radius, string label)
    {
        Assert.That(reachable.Contains(FindConnectedGridCell(map, point, radius)), Is.True, $"{label} is isolated from police spawn.");
    }

    private static (int X, int Y) FindConnectedGridCell(GameMap map, PointF point, float radius)
    {
        var nearby = new List<Obstacle>();
        var baseX = (int)MathF.Round(point.X / GridStep);
        var baseY = (int)MathF.Round(point.Y / GridStep);
        for (var ring = 0; ring <= 2; ring++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    var x = (baseX + dx) * GridStep;
                    var y = (baseY + dy) * GridStep;
                    var clear = true;
                    for (var step = 0; step <= 12; step++)
                    {
                        var t = step / 12f;
                        if (map.IsMovementPositionBlocked(point.X + (x - point.X) * t, point.Y + (y - point.Y) * t, radius, nearby))
                        {
                            clear = false;
                            break;
                        }
                    }
                    if (clear) return (baseX + dx, baseY + dy);
                }
            }
        }
        Assert.Fail($"No walkable grid connection around {point} for radius {radius}.");
        return default;
    }
}
