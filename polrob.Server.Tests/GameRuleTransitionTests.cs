using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using polrob.Server.Controllers;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRuleTransitionTests
{
    private const float PlayerRadius = 25f;
    private const float PlayerSpeed = 4f;
    private const string RoomId = "rule-test-room";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void LargeDiagonalInputAndDelayedTickCannotMoveFasterThanTheServerLimit()
    {
        var server = CreateServerWithoutNetworkSockets();
        var room = new GameSession(256) { GamePhase = GamePhase.Playing };
        var start = FindOpenSquare(room.Map);
        var player = CreatePlayer("runner", PlayerRole.Robber, start.X, start.Y);
        var loginToken = CreateLoginSession(player.Id);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 32001);
        var session = AddPlayer(room, player, loginToken, "movement-secret", endpoint);

        try
        {
            Invoke(server, "HandleRoomMove", RoomId, room,
                new MoveRoomCommand(new PlayerMovementInput
                {
                    Id = player.Id, Token = "movement-secret", Sequence = 1, X = 1000f, Y = 1000f
                }, endpoint));

            var inputLength = MathF.Sqrt(session.InputX * session.InputX + session.InputY * session.InputY);
            Assert.That(inputLength, Is.LessThanOrEqualTo(1.0001f), "The client cannot amplify diagonal speed.");

            var beforeShortTick = (player.X, player.Y);
            Invoke(server, "SimulateAuthoritativeMovement", room, TimeSpan.FromMilliseconds(50), session.LastMovementInputAtUtc);
            var shortTickDistance = Distance(beforeShortTick, (player.X, player.Y));
            Assert.That(shortTickDistance, Is.InRange(0.01f, 12.01f),
                "At 240 map units per second, a 50 ms tick may travel at most 12 units.");

            Invoke(server, "HandleRoomMove", RoomId, room,
                new MoveRoomCommand(new PlayerMovementInput
                {
                    Id = player.Id, Token = "movement-secret", Sequence = 2, X = 1000f, Y = 1000f
                }, endpoint));
            var beforeDelayedTick = (player.X, player.Y);
            Invoke(server, "SimulateAuthoritativeMovement", room, TimeSpan.FromSeconds(5), session.LastMovementInputAtUtc);
            Assert.That(Distance(beforeDelayedTick, (player.X, player.Y)), Is.LessThanOrEqualTo(24.01f),
                "A delayed server tick must not apply five seconds of movement at once.");
        }
        finally
        {
            Logout(loginToken);
        }
    }

    [Test]
    public void ExpiredMovementInputStopsThePlayer()
    {
        var server = CreateServerWithoutNetworkSockets();
        var room = new GameSession(256) { GamePhase = GamePhase.Playing };
        var start = FindOpenSquare(room.Map);
        var player = CreatePlayer("runner", PlayerRole.Robber, start.X, start.Y);
        var session = AddPlayer(room, player);
        session.InputX = 1f;
        session.LastMovementInputAtUtc = DateTime.UtcNow.AddSeconds(-1);
        player.IsMoving = true;

        Invoke(server, "SimulateAuthoritativeMovement", room, TimeSpan.FromMilliseconds(50), DateTime.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That((player.X, player.Y), Is.EqualTo(start));
            Assert.That(player.IsMoving, Is.False);
            Assert.That((session.InputX, session.InputY), Is.EqualTo((0f, 0f)));
        });
    }

    [Test]
    public void ServerMovementStopsAtARealSolidCollider()
    {
        var server = CreateServerWithoutNetworkSockets();
        var room = new GameSession(256) { GamePhase = GamePhase.Playing };
        var start = FindOpenPositionWithBlockedStep(room.Map);
        var player = CreatePlayer("runner", PlayerRole.Robber, start.X, start.Y);
        var session = AddPlayer(room, player);
        session.InputX = 1f;
        var now = DateTime.UtcNow;
        session.LastMovementInputAtUtc = now;

        Invoke(server, "SimulateAuthoritativeMovement", room, TimeSpan.FromMilliseconds(50), now);

        Assert.Multiple(() =>
        {
            Assert.That((player.X, player.Y), Is.EqualTo(start), "The next 12-unit step enters a solid map collider.");
            Assert.That(player.IsMoving, Is.False);
        });
    }

    [Test]
    public void ArrestLocksBothPlayersThenJailsTheRobberOnlyAfterCompletion()
    {
        var server = CreateServerWithoutNetworkSockets();
        var room = new GameSession(256) { GamePhase = GamePhase.Playing };
        var police = CreatePlayer("officer", PlayerRole.Police, 0f, 0f);
        var robber = CreatePlayer("suspect", PlayerRole.Robber, 0f, 0f);
        police.Angle = -90f; // Face east toward the robber.
        var policeSession = AddPlayer(room, police);
        var robberSession = AddPlayer(room, robber);
        PlaceVisiblePairAndDetectArrest(server, room, police, robber);

        Assert.That(room.ActiveArrestsByRobberId.ContainsKey(robber.Id), Is.True);
        Assert.That(robber.IsJailed, Is.False, "Starting an arrest is not the same as completing it.");

        var policePosition = (police.X, police.Y);
        var robberPosition = (robber.X, robber.Y);
        policeSession.InputX = robberSession.InputX = 1f;
        var now = DateTime.UtcNow;
        policeSession.LastMovementInputAtUtc = robberSession.LastMovementInputAtUtc = now;
        Invoke(server, "SimulateAuthoritativeMovement", room, TimeSpan.FromMilliseconds(50), now);
        Assert.Multiple(() =>
        {
            Assert.That((police.X, police.Y), Is.EqualTo(policePosition));
            Assert.That((robber.X, robber.Y), Is.EqualTo(robberPosition));
        });

        room.ActiveArrestsByRobberId[robber.Id].CompletesAtUtc = DateTime.UtcNow.AddMinutes(1);
        Invoke(server, "CompletePendingArrests", room);
        Assert.That(robber.IsJailed, Is.False, "A pending arrest cannot complete before its deadline.");

        room.ActiveArrestsByRobberId[robber.Id].CompletesAtUtc = DateTime.UtcNow.AddMilliseconds(-1);
        Invoke(server, "CompletePendingArrests", room);
        Assert.Multiple(() =>
        {
            Assert.That(robber.IsJailed, Is.True);
            Assert.That(room.ActiveArrestsByRobberId, Is.Empty);
            Assert.That(room.JailEntryTimes.ContainsKey(robber.Id), Is.True);
            Assert.That(GameMap.ContainsPoint(room.Map.JailHoldingArea!, robber.X, robber.Y), Is.True);
        });

        robberPosition = (robber.X, robber.Y);
        robberSession.InputX = 1f;
        now = DateTime.UtcNow;
        robberSession.LastMovementInputAtUtc = now;
        Invoke(server, "SimulateAuthoritativeMovement", room, TimeSpan.FromMilliseconds(50), now);
        Assert.That((robber.X, robber.Y), Is.EqualTo(robberPosition), "A jailed robber cannot move.");
    }

    [Test]
    public void LeavingTheRescueAreaErasesProgressAndRequiresAFullNewCountdown()
    {
        var server = CreateServerWithoutNetworkSockets();
        var (room, prisoner, rescuer) = CreateJailbreakRoom();

        Invoke(server, "UpdateJailBreakProgress", RoomId, room);
        room.JailBreakStartedAtByRescuer[rescuer.Id] = DateTime.UtcNow.AddSeconds(-1);
        Invoke(server, "UpdateJailBreakProgress", RoomId, room);
        Assert.That(room.JailBreakProgressByRescuer[rescuer.Id], Is.InRange(0.2f, 0.8f));

        var far = FindOpenPositionFarFrom(room.Map, room.Map.JailRescueArea!.Center.X,
            room.Map.JailRescueArea.Center.Y);
        rescuer.X = far.X;
        rescuer.Y = far.Y;
        Invoke(server, "UpdateJailBreakProgress", RoomId, room);
        Assert.Multiple(() =>
        {
            Assert.That(room.JailBreakProgressByRescuer, Is.Empty);
            Assert.That(room.JailBreakStartedAtByRescuer, Is.Empty);
            Assert.That(prisoner.IsJailed, Is.True);
        });

        rescuer.X = room.Map.JailRescueArea.Center.X;
        rescuer.Y = room.Map.JailRescueArea.Center.Y;
        Invoke(server, "UpdateJailBreakProgress", RoomId, room);
        Assert.Multiple(() =>
        {
            Assert.That(room.JailBreakProgressByRescuer[rescuer.Id], Is.LessThan(0.1f));
            Assert.That(prisoner.IsJailed, Is.True);
        });
    }

    [Test]
    public void CompletedRescueReleasesPrisonerAtAWalkablePosition()
    {
        var server = CreateServerWithoutNetworkSockets();
        var (room, prisoner, rescuer) = CreateJailbreakRoom();

        Invoke(server, "UpdateJailBreakProgress", RoomId, room);
        room.JailBreakStartedAtByRescuer[rescuer.Id] = DateTime.UtcNow.AddSeconds(-3.1);
        Invoke(server, "UpdateJailBreakProgress", RoomId, room);

        Assert.Multiple(() =>
        {
            Assert.That(prisoner.IsJailed, Is.False);
            Assert.That(room.JailEntryTimes.ContainsKey(prisoner.Id), Is.False);
            Assert.That(room.JailBreakProgressByRescuer, Is.Empty);
            Assert.That(room.Map.IsMovementPositionBlocked(prisoner.X, prisoner.Y, prisoner.Radius, []), Is.False);
            Assert.That(GameMap.ContainsPoint(room.Map.JailHoldingArea!, prisoner.X, prisoner.Y), Is.False);
        });
    }

    private static GameNetworkServer CreateServerWithoutNetworkSockets() =>
        (GameNetworkServer)RuntimeHelpers.GetUninitializedObject(typeof(GameNetworkServer));

    private static Player CreatePlayer(string id, PlayerRole role, float x, float y) => new()
    {
        Id = id, RoomId = RoomId, Role = role, X = x, Y = y,
        Radius = PlayerRadius, Speed = PlayerSpeed
    };

    private static PlayerSession AddPlayer(
        GameSession room,
        Player player,
        string loginToken = "",
        string movementToken = "",
        IPEndPoint? endpoint = null)
    {
        var session = new PlayerSession
        {
            PlayerState = player,
            Writer = new BinaryWriter(Stream.Null),
            ConnectionId = $"connection-{player.Id}",
            SessionToken = loginToken,
            MovementSessionToken = movementToken,
            UdpEndPoint = endpoint
        };
        room.Sessions[player.Id] = session;
        return session;
    }

    private static (float X, float Y) FindOpenSquare(GameMap map)
    {
        var nearby = new List<Obstacle>();
        for (var y = 100f; y < map.Height - 100f; y += 40f)
        for (var x = 100f; x < map.Width - 100f; x += 40f)
        {
            if (!map.IsMovementPositionBlocked(x, y, PlayerRadius, nearby) &&
                !map.IsMovementPositionBlocked(x + 40f, y + 40f, PlayerRadius, nearby) &&
                !map.IsMovementPositionBlocked(x + 40f, y, PlayerRadius, nearby) &&
                !map.IsMovementPositionBlocked(x, y + 40f, PlayerRadius, nearby))
            {
                return (x, y);
            }
        }
        throw new AssertionException("No open square was found on the active map.");
    }

    private static (float X, float Y) FindOpenPositionWithBlockedStep(GameMap map)
    {
        var nearby = new List<Obstacle>();
        for (var y = 100f; y < map.Height - 100f; y += 8f)
        for (var x = 100f; x < map.Width - 100f; x += 8f)
        {
            if (!map.IsMovementPositionBlocked(x, y, PlayerRadius, nearby) &&
                map.IsMovementPositionBlocked(x + 12f, y, PlayerRadius, nearby))
            {
                return (x, y);
            }
        }
        throw new AssertionException("No walkable point next to a solid collider was found.");
    }

    private static void PlaceVisiblePairAndDetectArrest(
        GameNetworkServer server, GameSession room, Player police, Player robber)
    {
        var nearby = new List<Obstacle>();
        for (var y = 100f; y < room.Map.Height - 100f; y += 40f)
        for (var x = 100f; x < room.Map.Width - 180f; x += 40f)
        {
            if (room.Map.IsMovementPositionBlocked(x, y, PlayerRadius, nearby) ||
                room.Map.IsMovementPositionBlocked(x + 70f, y, PlayerRadius, nearby) ||
                room.Map.FindBushContainingPoint(x + 70f, y) != null)
            {
                continue;
            }

            police.X = x;
            police.Y = y;
            robber.X = x + 70f;
            robber.Y = y;
            Invoke(server, "DetectRobbersForArrest", room);
            if (room.ActiveArrestsByRobberId.ContainsKey(robber.Id)) return;
        }
        throw new AssertionException("No visible walkable police/robber pair was found on the active map.");
    }

    private static (GameSession Room, Player Prisoner, Player Rescuer) CreateJailbreakRoom()
    {
        var room = new GameSession(256) { GamePhase = GamePhase.Playing };
        var holding = room.Map.GetJailHoldingPosition(0, 1, PlayerRadius);
        var prisoner = CreatePlayer("prisoner", PlayerRole.Robber, holding.X, holding.Y);
        prisoner.IsJailed = true;
        var rescueArea = room.Map.JailRescueArea!;
        var rescuer = CreatePlayer("rescuer", PlayerRole.Robber, rescueArea.Center.X, rescueArea.Center.Y);
        AddPlayer(room, prisoner);
        AddPlayer(room, rescuer);
        room.JailEntryTimes[prisoner.Id] = DateTime.UtcNow.AddSeconds(-10);
        return (room, prisoner, rescuer);
    }

    private static (float X, float Y) FindOpenPositionFarFrom(GameMap map, float x, float y)
    {
        var nearby = new List<Obstacle>();
        for (var candidateY = 100f; candidateY < map.Height - 100f; candidateY += 40f)
        for (var candidateX = 100f; candidateX < map.Width - 100f; candidateX += 40f)
        {
            if (Distance((candidateX, candidateY), (x, y)) > 500f &&
                !map.IsMovementPositionBlocked(candidateX, candidateY, PlayerRadius, nearby))
            {
                return (candidateX, candidateY);
            }
        }
        throw new AssertionException("No walkable position away from the rescue area was found.");
    }

    private static float Distance((float X, float Y) from, (float X, float Y) to) =>
        MathF.Sqrt(MathF.Pow(to.X - from.X, 2f) + MathF.Pow(to.Y - from.Y, 2f));

    private static void Invoke(GameNetworkServer server, string methodName, params object[] arguments) =>
        typeof(GameNetworkServer).GetMethod(methodName, PrivateInstance)!.Invoke(server, arguments);

    private static string CreateLoginSession(string userId) => (string)typeof(AuthController)
        .GetMethod("CreateSession", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [userId])!;

    private static void Logout(string token) =>
        new AuthController(null!, null!, null!).Logout(new AuthController.LogoutRequest(token));
}
