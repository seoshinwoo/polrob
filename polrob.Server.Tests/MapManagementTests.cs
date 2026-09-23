using System.Drawing;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using polrob.Server.Controllers;
using NUnit.Framework;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class MapManagementTests
{
    [Test]
    public void NewMapIsDefaultAndClassicRemainsIndependent()
    {
        var current = new GameSession(8);
        var classic = new GameSession(8, MapRegistry.ClassicTown);
        var another = new GameSession(8);
        Assert.Multiple(() =>
        {
            Assert.That(current.Map.MapId, Is.EqualTo(MapRegistry.ChaseTown));
            Assert.That(new Game().MapId, Is.EqualTo(MapRegistry.ChaseTown));
            Assert.That(classic.Map.PropLayouts, Is.SameAs(CanvaMapLayout.Props));
            Assert.That(current.Map.PropLayouts, Is.SameAs(ChaseTownLayout.Props));
            Assert.That(classic.Map.PoliceStation.ImageFileName, Does.StartWith("MapAssets/"));
            Assert.That(current.Map.PoliceStation.ImageFileName, Does.StartWith("ChaseTownV7/"));
            Assert.That(another.Map.PoliceStation, Is.Not.SameAs(current.Map.PoliceStation));
            Assert.That(another.Map.Obstacles[0], Is.Not.SameAs(current.Map.Obstacles[0]));
            Assert.That(classic.Map.JailRescueArea, Is.Null);
            Assert.That(current.Map.JailRescueArea, Is.Not.Null);
        });
    }

    [Test]
    public async Task InvalidMapCannotCreateRoomOrPhysics()
    {
        var service = new GameRoomService(null!, null!, NullLogger<GameRoomService>.Instance);
        Assert.That((await service.CreateRoom("unused", mapId: "unknown")).Success, Is.False);
        Assert.Throws<ArgumentException>(() => new GameMap("unknown"));
        Assert.Throws<ArgumentException>(() => new GameSession(8, "unknown"));
        Assert.That(MapRegistry.Contains(JsonSerializer.Deserialize<GameJoinRequest>("{}")!.MapId), Is.False,
            "Clients predating map selection must not silently join with the wrong physics.");
    }

    [TestCase(MapRegistry.ChaseTown)]
    [TestCase(MapRegistry.ClassicTown)]
    public async Task RoomMapEndpointRequiresMembershipAndReturnsAuthoritativeId(string mapId)
    {
        var bots = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var service = new GameRoomService(null!, bots, NullLogger<GameRoomService>.Instance);
        var host = bots.Create("host", PlayerRole.Police);
        var room = await service.CreateRoom(host.Id, mapId: mapId);
        var controller = new GameController(service, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.That(controller.GetRoomStatus(room.RoomId!).Result, Is.TypeOf<UnauthorizedResult>());
        foreach (var userId in new[] { "not-a-member", host.Id })
        {
            var token = (string)typeof(AuthController).GetMethod("CreateSession", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [userId])!;
            try
            {
                controller.Request.Headers.Authorization = $"Bearer {token}";
                var result = controller.GetRoomStatus(room.RoomId!).Result;
                if (userId != host.Id)
                    Assert.That((result as StatusCodeResult)?.StatusCode, Is.EqualTo(403));
                else
                {
                    Assert.That(((result as OkObjectResult)?.Value as ServerResponse)?.MapId, Is.EqualTo(mapId));
                    Assert.That(controller.GetRoomStatus("missing-room").Result, Is.TypeOf<NotFoundObjectResult>());
                }
            }
            finally { new AuthController(null!, null!, null!).Logout(new AuthController.LogoutRequest(token)); }
        }
    }

    [TestCase(MapRegistry.ChaseTown)]
    [TestCase(MapRegistry.ClassicTown)]
    public async Task RoomMapSurvivesJoinStartAndReplay(string mapId)
    {
        var bots = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var service = new GameRoomService(null!, bots, NullLogger<GameRoomService>.Instance);
        var host = bots.Create("map-host", PlayerRole.Police);
        var robber = bots.Create("map-robber", PlayerRole.Robber);
        var created = await service.CreateRoom(host.Id, mapId: mapId);
        var joined = await service.JoinCustomGame(robber.Id, created.RoomCode!);
        var started = service.StartGameIfMatched(created.RoomId!);
        var finished = service.CompleteGame(created.RoomId!);
        var replay = await service.RejoinCustomRoomForReplay(created.RoomId!, host.Id, PlayerRole.Police);
        foreach (var status in new[] { created, joined, started, finished, replay, service.GetRoomStatus(created.RoomId!) })
        {
            Assert.That(status.Success, Is.True, status.Message);
            Assert.That(status.MapId, Is.EqualTo(mapId));
            Assert.That(JsonSerializer.Deserialize<ServerResponse>(JsonSerializer.Serialize(status))!.MapId,
                Is.EqualTo(mapId));
        }
        Assert.That((await service.CreateRoom(host.Id)).MapId, Is.EqualTo(MapRegistry.DefaultId),
            "Creating a classic room must not change the next room's default.");
    }

    [TestCase(MapRegistry.ChaseTown)]
    [TestCase(MapRegistry.ClassicTown)]
    public void ServerUsesSelectedMapForCollisionJailAndSpawns(string mapId)
    {
        var session = new GameSession(8, mapId);
        var map = session.Map;
        foreach (var role in new[] { PlayerRole.Police, PlayerRole.Robber })
        for (var slot = 0; slot < 6; slot++)
        {
            var spawn = map.GetSpawnPosition(role, slot, 25);
            Assert.That(Server<bool>("IsMovementPositionBlocked", session, spawn.X, spawn.Y, 25f, new List<Obstacle>()), Is.False);
        }
        var station = map.PoliceStation.CollisionCenter;
        Assert.That(Server<bool>("IsMovementPositionBlocked", session, station.X, station.Y, 25f, new List<Obstacle>()), Is.True);
        var jailBounds = GameMap.GetBuildingCollisionBounds(map.Jail);
        var rescuePoint = map.JailRescueArea?.Center ?? new PointF(map.Jail.CollisionCenter.X, jailBounds.Bottom + 30);
        var player = new Player { X = rescuePoint.X, Y = rescuePoint.Y, Radius = 25 };
        Assert.That(Server<bool>("IsTouchingOrNearJail", session, player), Is.True);
        player.X = 25; player.Y = map.Height - 25;
        Assert.That(Server<bool>("IsTouchingOrNearJail", session, player), Is.False);
        for (var i = 0; i < 4; i++)
        {
            var release = Server<(float X, float Y)>("GetJailReleasePosition", session, 25f, i);
            Assert.That(map.IsMovementPositionBlocked(release.X, release.Y, 25, []), Is.False);
            var holding = map.GetJailHoldingPosition(i, 4, 25);
            Assert.That(holding.X, Is.InRange(map.Jail.LeftTop.X, map.Jail.RightBottom.X));
            Assert.That(holding.Y, Is.InRange(map.Jail.LeftTop.Y, map.Jail.RightBottom.Y));
        }
    }

    [Test]
    public void ApprovedGroundAndRemovalsArePreserved()
    {
        // Every building's center stands on concrete, not a sage/concrete patchwork.
        foreach (var prop in ChaseTownLayout.Placements.Where(p => p.BuildingType != null))
            Assert.That(ChaseTownLayout.Ground[(int)prop.Center.X / 80, (int)prop.Center.Y / 80], Is.EqualTo(GroundTile.Paving), prop.Id);
        for (var y = 30; y <= 40; y++)
        for (var x = 22; x < 32; x++)
            Assert.That(ChaseTownLayout.Ground[x, y], Is.EqualTo(GroundTile.Grass));
        foreach (var (id, x, y) in new[]
        {
            ("tree",756,202), ("bush",740,241), ("tree",978,61),
            ("tree",129,832), ("bush",329,727), ("tree",986,648),
            ("crate",588,262), ("bush",916,246), ("bush",988,240)
        })
            Assert.That(ChaseTownLayout.Placements.Any(p => p.AssetId == id &&
                Math.Abs(p.Center.X / 2.5f - x) < 10 && Math.Abs(p.Center.Y / 2.5f - y) < 10), Is.False);
    }

    [Test]
    public void PoliceStationHasWalkableRearAndFrontPassages()
    {
        var map = new GameMap();
        Assert.That(map.PoliceStation.Center.Y / 2.5f, Is.EqualTo(152f));

        // A radius-25 player can cross the two narrow strips without clipping
        // either the station or the surrounding wall sections.
        foreach (var (fromX, toX, y) in new[] { (480f, 725f, 62f), (480f, 725f, 252f) })
        for (var x = fromX; x <= toX; x += 2f)
            Assert.That(map.IsMovementPositionBlocked(x * 2.5f, y * 2.5f, 25f, []), Is.False,
                $"station passage {x},{y}");
    }

    [TestCase(25f)]
    [TestCase(50f)]
    public void AuthoredCrateAndWallPassagesHaveContinuousClearance(float radius)
    {
        var map = new GameMap();
        // Warehouse: walk between each pair of crates, the building, and the long wall.
        foreach (var y in new[] { 1112f, 1196f })
        for (var x = 490f; x <= 570; x += 2)
            Assert.That(map.IsMovementPositionBlocked(x * 2.5f, y * 2.5f, radius, []), Is.False, $"crate gap {x},{y}");
        for (var y = 1045f; y <= 1270; y += 2)
            Assert.That(map.IsMovementPositionBlocked(570 * 2.5f, y * 2.5f, radius, []), Is.False, $"wall corridor {y}");
    }

    private static T Server<T>(string method, params object[] args) =>
        (T)typeof(GameNetworkServer).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args)!;
}
