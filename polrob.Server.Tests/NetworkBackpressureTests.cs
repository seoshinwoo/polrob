using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using polrob.Server.Network;
using polrob.Server.Operations;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class NetworkBackpressureTests
{
    [Test]
    public void DisconnectedQueuedJoinDoesNotCreateAGhostPlayer()
    {
        var server = (GameNetworkServer)RuntimeHelpers.GetUninitializedObject(typeof(GameNetworkServer));
        using var client = new TcpClient();
        client.Close();
        using var writer = new BinaryWriter(new MemoryStream());
        var room = new GameSession(8);
        typeof(GameNetworkServer).GetMethod("HandleRoomJoin", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(server, new object[] { "room", room, new JoinRoomCommand(
                new Player { Id = "closed-player" }, client, writer, "connection", "token") });
        Assert.That(room.Sessions, Is.Empty);
    }

    [Test]
    public void SlowReaderQueueHasBothMessageAndByteLimits()
    {
        using var client = new TcpClient();
        var failures = 0;
        var peer = new TcpPeer(client, 2, 1024, TimeSpan.FromSeconds(1), () => failures++, () => { });
        Assert.That(peer.TrySend(TcpMessageType.GameState, "one"), Is.True);
        Assert.That(peer.TrySend(TcpMessageType.GameState, "two"), Is.True);
        Assert.That(peer.TrySend(TcpMessageType.GameState, "three"), Is.False);
        Assert.That(failures, Is.EqualTo(1));

        using var other = new TcpClient();
        var bytes = new TcpPeer(other, 100, 20, TimeSpan.FromSeconds(1), () => { }, () => { });
        Assert.That(bytes.TrySend(TcpMessageType.GameState, new string('x', 21)), Is.False);
    }

    [Test]
    public void FailedRecordAcceptanceRetriesTheSameSnapshotAndDoesNotMarkItCommitted()
    {
        var server = (GameNetworkServer)RuntimeHelpers.GetUninitializedObject(typeof(GameNetworkServer));
        var queue = new RejectThenAcceptQueue();
        Set(server, "_gameRecordQueue", queue);
        Set(server, "_logger", NullLogger<GameNetworkServer>.Instance);
        var operations = new ServerOperations();
        Set(server, "_operations", operations);
        var room = new GameSession(8)
        {
            GameRecordId = "result", GameStartedAtUtc = DateTime.UtcNow.AddSeconds(-60),
            WinnerRole = PlayerRole.Police, ElapsedGameTime = 60,
            StartingPolicePlayerIds = new[] { "police" }, StartingRobberPlayerIds = new[] { "robber" }
        };
        var method = typeof(GameNetworkServer).GetMethod("TryEnqueueCompletedGameRecord", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(method.Invoke(server, new object[] { "room", room, DateTime.UtcNow }), Is.False);
        var first = room.PendingGameRecord;
        Assert.That(room.GameRecordEnqueueAttempted, Is.False);
        Assert.That(operations.HasUnpersistedResults, Is.True);
        Assert.That(method.Invoke(server, new object[] { "room", room, DateTime.UtcNow.AddSeconds(10) }), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(queue.Last, Is.SameAs(first));
            Assert.That(room.PendingGameRecord, Is.Null);
            Assert.That(room.GameRecordEnqueueAttempted, Is.True);
            Assert.That(operations.HasUnpersistedResults, Is.False);
        });
    }

    [Test]
    public void MovementCoalescingKeepsHighestSequenceDespiteOutOfOrderArrival()
    {
        var server = (GameNetworkServer)RuntimeHelpers.GetUninitializedObject(typeof(GameNetworkServer));
        var token = (string)typeof(polrob.Server.Controllers.AuthController)
            .GetMethod("CreateSession", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { "player" })!;
        try
        {
            var room = new GameSession(8);
            var endpoint = new IPEndPoint(IPAddress.Loopback, 33333);
            room.Sessions["player"] = new PlayerSession
            {
                SessionToken = token, MovementSessionToken = "movement", UdpEndPoint = endpoint,
                PlayerState = new Player { Id = "player" }
            };
            foreach (var sequence in new ulong[] { 5, 3 })
            {
                room.Commands.Writer.TryWrite(new MoveRoomCommand(new PlayerMovementInput
                { Id = "player", Token = "movement", Sequence = sequence, X = 1 }, endpoint));
                room.QueuedCommandCount++;
            }
            typeof(GameNetworkServer).GetMethod("DrainRoomCommands", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(server, new object[] { "room", room });
            Assert.That(room.Sessions["player"].LastMovementInputSequence, Is.EqualTo(5));
        }
        finally { new polrob.Server.Controllers.AuthController(null!, null!, null!).Logout(new(token)); }
    }

    [Test]
    public async Task LobbyCompletionWaitsForDurableAcceptanceEvenAfterAllPlayersDisconnect()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var rooms = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var police = identities.Create("police", PlayerRole.Police);
        var robber = identities.Create("robber", PlayerRole.Robber);
        var created = await rooms.CreateRoom(police.Id);
        await rooms.JoinCustomGame(robber.Id, created.RoomCode!);
        rooms.StartGameIfMatched(created.RoomId!);
        var server = (GameNetworkServer)RuntimeHelpers.GetUninitializedObject(typeof(GameNetworkServer));
        Set(server, "_gameRoomService", rooms);
        Set(server, "_gameRecordQueue", new RejectThenAcceptQueue());
        Set(server, "_logger", NullLogger<GameNetworkServer>.Instance);
        var record = new CompletedGameRecord("game", created.RoomId!, PlayerRole.Police,
            new[] { police.Id }, new[] { robber.Id }, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, 60);
        var room = new GameSession(8)
        {
            GamePhase = GamePhase.Ended, GameRecordId = record.Id, GameStartedAtUtc = record.StartedAtUtc,
            WinnerRole = record.WinnerRole, PendingGameRecord = record
        };
        var tick = typeof(GameNetworkServer).GetMethod("ProcessRoomStateSync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tick.Invoke(server, new object[] { created.RoomId!, room });
        Assert.That(rooms.IsGameInProgress(created.RoomId!), Is.True);
        Assert.That(room.PendingGameRecord, Is.Not.Null);
        tick.Invoke(server, new object[] { created.RoomId!, room });
        Assert.That(rooms.IsGameInProgress(created.RoomId!), Is.False);
        Assert.That(room.PendingGameRecord, Is.Null);
    }

    private static void Set(GameNetworkServer server, string field, object value) => typeof(GameNetworkServer)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(server, value);
    private sealed class RejectThenAcceptQueue : IGameRecordQueue
    {
        private int _attempts;
        public CompletedGameRecord? Last;
        public bool TryEnqueue(CompletedGameRecord record) { Last = record; return ++_attempts > 1; }
    }
}
