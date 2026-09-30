using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameNetworkLifecycleTests
{
    [Test]
    public void LateLeaveCommandCannotRemoveReplacementGameSession()
    {
        var server = CreateServerWithoutSockets();
        SetField(server, "_gameRoomService", new GameRoomService(
            null!, null!, NullLogger<GameRoomService>.Instance));

        var room = new GameSession(8);
        room.Sessions["player-1"] = new PlayerSession
        {
            ConnectionId = "replacement-connection",
            PlayerState = new Player { Id = "player-1", Role = PlayerRole.Robber }
        };

        Invoke(server, "HandleRoomLeave", "room-1", room,
            new LeaveRoomCommand("player-1", "old-connection", PlayerRole.Robber));

        Assert.That(room.Sessions["player-1"].ConnectionId, Is.EqualTo("replacement-connection"));
    }

    [Test]
    public void EmptyRoomLoopStopsOnlyAfterGracePeriodAndWithNoQueuedCommands()
    {
        var server = CreateServerWithoutSockets();
        var rooms = new ConcurrentDictionary<string, GameSession>();
        SetField(server, "_gameSessions", rooms);
        var room = new GameSession(8) { HasHadPlayers = true };
        rooms["room-1"] = room;
        var now = DateTime.UtcNow;

        Assert.That(Invoke<bool>(server, "TryStopRoomLoop", "room-1", room, now), Is.False);
        Assert.That(rooms.ContainsKey("room-1"), Is.True);

        room.EmptySinceUtc = now.AddSeconds(-3);
        room.Commands.Writer.TryWrite(new MoveRoomCommand(
            new PlayerMovementInput { Id = "player-1" },
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 32001)));
        Assert.That(Invoke<bool>(server, "TryStopRoomLoop", "room-1", room, now), Is.False);
        Assert.That(room.EmptySinceUtc, Is.Null);

        Assert.That(room.Commands.Reader.TryRead(out _), Is.True);
        room.EmptySinceUtc = now.AddSeconds(-3);
        Assert.That(Invoke<bool>(server, "TryStopRoomLoop", "room-1", room, now), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rooms.ContainsKey("room-1"), Is.False);
            Assert.That(room.IsStopping, Is.True);
            Assert.That(room.Commands.Writer.TryWrite(new MoveRoomCommand(
                new PlayerMovementInput(),
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 32001))), Is.False);
        });
    }

    [Test]
    public async Task EmptyPlayingRoomLoopAbandonsLobbyRoomAfterReconnectGracePeriod()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var service = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var police = identities.Create("police", PlayerRole.Police);
        var robber = identities.Create("robber", PlayerRole.Robber);
        var created = await service.CreateRoom(police.Id);
        Assert.That((await service.JoinCustomGame(robber.Id, created.RoomCode!)).Success, Is.True);
        Assert.That(service.StartGameIfMatched(created.RoomId!).Success, Is.True);

        var server = CreateServerWithoutSockets();
        var rooms = new ConcurrentDictionary<string, GameSession>();
        var gameSession = new GameSession(8)
        {
            GamePhase = GamePhase.Playing,
            HasHadPlayers = true
        };
        rooms[created.RoomId!] = gameSession;
        SetField(server, "_gameSessions", rooms);
        SetField(server, "_gameRoomService", service);

        var now = DateTime.UtcNow;
        Assert.That(Invoke<bool>(server, "TryStopRoomLoop", created.RoomId!, gameSession, now), Is.False);
        Assert.That(service.IsGameInProgress(created.RoomId!), Is.True,
            "The room must remain available for a reconnect during the grace period.");

        gameSession.EmptySinceUtc = now.AddSeconds(-3);
        gameSession.Commands.Writer.TryWrite(new MoveRoomCommand(
            new PlayerMovementInput { Id = police.Id },
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 32002)));
        Assert.That(Invoke<bool>(server, "TryStopRoomLoop", created.RoomId!, gameSession, now), Is.False);
        Assert.That(service.IsGameInProgress(created.RoomId!), Is.True,
            "A queued reconnect/input command must prevent abandonment.");
        Assert.That(gameSession.Commands.Reader.TryRead(out _), Is.True);
        gameSession.EmptySinceUtc = now.AddSeconds(-3);
        Assert.That(Invoke<bool>(server, "TryStopRoomLoop", created.RoomId!, gameSession, now), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rooms.ContainsKey(created.RoomId!), Is.False);
            Assert.That(service.GetRoomStatus(created.RoomId!).Success, Is.False,
                "A game with no TCP participants cannot stay permanently in progress.");
            Assert.That(service.GetLoadSnapshot().TotalRooms, Is.Zero);
            Assert.That(gameSession.GameRecordEnqueueAttempted, Is.False);
            Assert.That(gameSession.WinnerRole, Is.Null);
        });
    }

    [Test]
    public async Task EndedRoomLoopKeepsCompletedCustomRoomForReplay()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var service = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var police = identities.Create("police", PlayerRole.Police);
        var robber = identities.Create("robber", PlayerRole.Robber);
        var created = await service.CreateRoom(police.Id);
        Assert.That((await service.JoinCustomGame(robber.Id, created.RoomCode!)).Success, Is.True);
        Assert.That(service.StartGameIfMatched(created.RoomId!).Success, Is.True);
        Assert.That(service.CompleteGame(created.RoomId!).Success, Is.True);

        var server = CreateServerWithoutSockets();
        var rooms = new ConcurrentDictionary<string, GameSession>();
        var gameSession = new GameSession(8)
        {
            GamePhase = GamePhase.Ended,
            HasHadPlayers = true,
            EmptySinceUtc = DateTime.UtcNow.AddSeconds(-3)
        };
        rooms[created.RoomId!] = gameSession;
        SetField(server, "_gameSessions", rooms);
        SetField(server, "_gameRoomService", service);

        Assert.That(Invoke<bool>(server, "TryStopRoomLoop", created.RoomId!, gameSession, DateTime.UtcNow), Is.True);
        Assert.That(service.AbandonGameAfterDisconnect(created.RoomId!), Is.False);
        Assert.That(service.AbandonGameAfterDisconnect(created.RoomId!), Is.False);
        var replayStatus = service.GetRoomStatus(created.RoomId!);
        Assert.Multiple(() =>
        {
            Assert.That(replayStatus.Success, Is.True);
            Assert.That(replayStatus.Players.Select(player => player.Id),
                Is.EquivalentTo(new[] { police.Id, robber.Id }));
            Assert.That(service.IsGameInProgress(created.RoomId!), Is.False);
        });
    }

    private static GameNetworkServer CreateServerWithoutSockets() =>
        (GameNetworkServer)RuntimeHelpers.GetUninitializedObject(typeof(GameNetworkServer));

    private static void SetField(GameNetworkServer server, string name, object value) =>
        typeof(GameNetworkServer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, value);

    private static void Invoke(GameNetworkServer server, string name, params object[] args) =>
        typeof(GameNetworkServer).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(server, args);

    private static T Invoke<T>(GameNetworkServer server, string name, params object[] args) =>
        (T)typeof(GameNetworkServer).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(server, args)!;
}
