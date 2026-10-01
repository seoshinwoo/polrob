using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using polrob.Server.Controllers;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameNetworkSocketTests
{
    [Test]
    public async Task ReconnectReplacesTcpSessionAndOldConnectionCannotRemoveIt()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var roomService = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var player = identities.Create("reconnecting-player", PlayerRole.Police);
        var created = await roomService.CreateRoom(player.Id);
        Assert.That(created.Success, Is.True, created.Message);
        var roomId = created.RoomId!;
        var registry = new ActiveGameParticipantRegistry();
        var token = CreateLoginSession(player.Id);
        var server = CreateServer(roomService, registry);
        TcpClient? first = null;
        TcpClient? replacement = null;

        try
        {
            await server.StartAsync(CancellationToken.None);
            var port = await WaitForTcpPortAsync(server);

            first = await ConnectAsync(port);
            WriteFrame(first, TcpMessageType.Join, JsonSerializer.Serialize(new GameJoinRequest
            {
                SessionToken = token, RoomId = roomId, MapId = MapRegistry.DefaultId
            }));
            var firstMovementToken = await ReadUntilTypeAsync(first, TcpMessageType.MovementSession);
            Assert.That(firstMovementToken, Is.Not.Empty);
            Assert.That(registry.TryGetConnectionId(roomId, player.Id, out var firstConnectionId), Is.True);

            replacement = await ConnectAsync(port);
            WriteFrame(replacement, TcpMessageType.Join, JsonSerializer.Serialize(new GameJoinRequest
            {
                SessionToken = token, RoomId = roomId, MapId = MapRegistry.DefaultId
            }));
            var secondMovementToken = await ReadUntilTypeAsync(replacement, TcpMessageType.MovementSession);
            Assert.That(registry.TryGetConnectionId(roomId, player.Id, out var secondConnectionId), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(secondConnectionId, Is.Not.EqualTo(firstConnectionId));
                Assert.That(secondMovementToken, Is.Not.EqualTo(firstMovementToken));
            });

            // The replaced socket's next heartbeat is rejected, producing a late Leave.
            WriteFrame(first, TcpMessageType.Heartbeat, "old-connection");
            await WaitUntilAsync(() => GetTcpConnectionCount(server) == 1, TimeSpan.FromSeconds(3));
            var room = GetGameSessions(server)[roomId];
            await WaitUntilAsync(() => room.QueuedCommandCount == 0, TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(room.Sessions[player.Id].ConnectionId, Is.EqualTo(secondConnectionId));
                Assert.That(room.Sessions[player.Id].MovementSessionToken, Is.EqualTo(secondMovementToken));
                Assert.That(registry.TryGetConnectionId(roomId, player.Id, out var activeConnectionId), Is.True);
                Assert.That(activeConnectionId, Is.EqualTo(secondConnectionId));
            });

            WriteFrame(replacement, TcpMessageType.Heartbeat, "current-connection");
            Assert.That(await ReadUntilTypeAsync(replacement, TcpMessageType.HeartbeatAcknowledged),
                Is.EqualTo("current-connection"));
        }
        finally
        {
            first?.Dispose();
            replacement?.Dispose();
            await StopAndDisposeAsync(server);
            Logout(token);
        }
    }

    [Test]
    public async Task OversizedFrameAndUnauthenticatedJoinAreClosedBeforeRoomCreation()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var roomService = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var registry = new ActiveGameParticipantRegistry();
        var server = CreateServer(roomService, registry);

        try
        {
            await server.StartAsync(CancellationToken.None);
            var port = await WaitForTcpPortAsync(server);

            using (var oversized = await ConnectAsync(port))
            {
                using var writer = new BinaryWriter(oversized.GetStream(), Encoding.UTF8, leaveOpen: true);
                writer.Write(1 + 5 + 4097); // More than the 4096-byte inbound payload limit.
                writer.Flush();
                await AssertRemoteClosedAsync(oversized);
            }

            using (var unauthenticated = await ConnectAsync(port))
            {
                WriteFrame(unauthenticated, TcpMessageType.Join, JsonSerializer.Serialize(new GameJoinRequest
                {
                    SessionToken = "not-a-session", RoomId = "invented-room", MapId = MapRegistry.DefaultId
                }));
                await AssertRemoteClosedAsync(unauthenticated);
            }

            using (var heartbeatBeforeJoin = await ConnectAsync(port))
            {
                WriteFrame(heartbeatBeforeJoin, TcpMessageType.Heartbeat, "keep-alive-without-login");
                await AssertRemoteClosedAsync(heartbeatBeforeJoin);
            }

            Assert.That(GetGameSessions(server), Is.Empty);
            Assert.That(registry.IsActive("invented-room", "invented-player"), Is.False);
        }
        finally
        {
            await StopAndDisposeAsync(server);
        }
    }

    [Test]
    public async Task StartedRoomIsRemovedWhenAllTcpPlayersStayDisconnectedPastGracePeriod()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var roomService = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var police = identities.Create("police", PlayerRole.Police);
        var robber = identities.Create("robber", PlayerRole.Robber);
        var created = await roomService.CreateRoom(police.Id);
        Assert.That((await roomService.JoinCustomGame(robber.Id, created.RoomCode!)).Success, Is.True);
        Assert.That(roomService.StartGameIfMatched(created.RoomId!).Success, Is.True);

        var roomId = created.RoomId!;
        var policeToken = CreateLoginSession(police.Id);
        var robberToken = CreateLoginSession(robber.Id);
        var server = CreateServer(roomService, new ActiveGameParticipantRegistry());
        TcpClient? policeClient = null;
        TcpClient? robberClient = null;

        try
        {
            await server.StartAsync(CancellationToken.None);
            var port = await WaitForTcpPortAsync(server);

            policeClient = await ConnectAsync(port);
            WriteFrame(policeClient, TcpMessageType.Join, JsonSerializer.Serialize(new GameJoinRequest
            {
                SessionToken = policeToken, RoomId = roomId, MapId = MapRegistry.DefaultId
            }));
            robberClient = await ConnectAsync(port);
            WriteFrame(robberClient, TcpMessageType.Join, JsonSerializer.Serialize(new GameJoinRequest
            {
                SessionToken = robberToken, RoomId = roomId, MapId = MapRegistry.DefaultId
            }));
            await ReadUntilTypeAsync(policeClient, TcpMessageType.MovementSession);
            await ReadUntilTypeAsync(robberClient, TcpMessageType.MovementSession);

            var room = GetGameSessions(server)[roomId];
            await WaitUntilAsync(() => room.GamePhase == GamePhase.Playing, TimeSpan.FromSeconds(8));
            Assert.That(roomService.IsGameInProgress(roomId), Is.True);

            policeClient.Dispose();
            robberClient.Dispose();
            policeClient = null;
            robberClient = null;
            await WaitUntilAsync(() => !GetGameSessions(server).ContainsKey(roomId), TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(roomService.IsGameInProgress(roomId), Is.False);
                Assert.That(roomService.GetRoomStatus(roomId).Success, Is.False);
            });
        }
        finally
        {
            policeClient?.Dispose();
            robberClient?.Dispose();
            await StopAndDisposeAsync(server);
            Logout(policeToken);
            Logout(robberToken);
        }
    }

    [Test]
    public async Task ConnectionCapRejectsExcessClientsAndReleasesSlotsAfterDisconnect()
    {
        var service = new GameRoomService(null!, null!, NullLogger<GameRoomService>.Instance);
        var server = CreateServer(service, new ActiveGameParticipantRegistry(), new()
        { ["GameNetwork:MaxTcpConnections"] = "1" });
        try
        {
            await server.StartAsync(CancellationToken.None);
            var port = await WaitForTcpPortAsync(server);
            using var first = await ConnectAsync(port);
            await WaitUntilAsync(() => GetTcpConnectionCount(server) == 1, TimeSpan.FromSeconds(2));
            using var excess = await ConnectAsync(port);
            await AssertRemoteClosedAsync(excess);
            Assert.That(GetTcpConnectionCount(server), Is.EqualTo(1));
            first.Close();
            await WaitUntilAsync(() => GetTcpConnectionCount(server) == 0, TimeSpan.FromSeconds(2));
            using var replacement = await ConnectAsync(port);
            await WaitUntilAsync(() => GetTcpConnectionCount(server) == 1, TimeSpan.FromSeconds(2));
        }
        finally { await StopAndDisposeAsync(server); }
    }

    [Test]
    public async Task PartialFrameCannotKeepAnUnauthenticatedConnectionForever()
    {
        var server = CreateServer(new GameRoomService(null!, null!, NullLogger<GameRoomService>.Instance),
            new ActiveGameParticipantRegistry(), new() { ["GameNetwork:TcpJoinTimeoutSeconds"] = "1" });
        try
        {
            await server.StartAsync(CancellationToken.None);
            using var client = await ConnectAsync(await WaitForTcpPortAsync(server));
            await client.GetStream().WriteAsync(new byte[] { 1, 0 }); // half of the frame length
            await AssertRemoteClosedAsync(client);
            await WaitUntilAsync(() => GetTcpConnectionCount(server) == 0, TimeSpan.FromSeconds(2));
            Assert.That(GetGameSessions(server), Is.Empty);
        }
        finally { await StopAndDisposeAsync(server); }
    }

    [Test]
    public async Task ShutdownCancelsIdleReadsAndClosesAllConnections()
    {
        var server = CreateServer(new GameRoomService(null!, null!, NullLogger<GameRoomService>.Instance),
            new ActiveGameParticipantRegistry(), new() { ["GameNetwork:TcpJoinTimeoutSeconds"] = "60" });
        try
        {
            await server.StartAsync(CancellationToken.None);
            using var client = await ConnectAsync(await WaitForTcpPortAsync(server));
            await WaitUntilAsync(() => GetTcpConnectionCount(server) == 1, TimeSpan.FromSeconds(2));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(deadline.Token);
            Assert.That(deadline.IsCancellationRequested, Is.False);
            await AssertRemoteClosedAsync(client);
            Assert.That(GetTcpConnectionCount(server), Is.Zero);
        }
        finally { server.Dispose(); }
    }

    [Test]
    public async Task ShutdownAbandonsAnUnfinishedGameWithoutInventingAResult()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var service = new GameRoomService(null!, identities, NullLogger<GameRoomService>.Instance);
        var player = identities.Create("shutdown-player", PlayerRole.Police);
        var created = await service.CreateRoom(player.Id);
        var robber = identities.Create("shutdown-robber", PlayerRole.Robber);
        await service.JoinCustomGame(robber.Id, created.RoomCode!);
        service.StartGameIfMatched(created.RoomId!);
        var token = CreateLoginSession(player.Id);
        var records = new CountingGameRecordQueue();
        var server = CreateServer(service, new ActiveGameParticipantRegistry(), queue: records);
        try
        {
            await server.StartAsync(CancellationToken.None);
            using var client = await ConnectAsync(await WaitForTcpPortAsync(server));
            WriteFrame(client, TcpMessageType.Join, JsonSerializer.Serialize(new GameJoinRequest
            { SessionToken = token, RoomId = created.RoomId!, MapId = MapRegistry.DefaultId }));
            await ReadUntilTypeAsync(client, TcpMessageType.MovementSession);
            var room = GetGameSessions(server)[created.RoomId!];
            room.GamePhase = GamePhase.Playing;
            room.GameStartedAtUtc = DateTime.UtcNow;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(deadline.Token);
            Assert.Multiple(() =>
            {
                Assert.That(records.Count, Is.Zero);
                Assert.That(GetGameSessions(server), Is.Empty);
                Assert.That(service.IsGameInProgress(created.RoomId!), Is.False);
                Assert.That(GetTcpConnectionCount(server), Is.Zero);
            });
        }
        finally { server.Dispose(); Logout(token); }
    }

    private sealed class CountingGameRecordQueue : IGameRecordQueue
    {
        public int Count;
        public bool TryEnqueue(CompletedGameRecord record) { Count++; return true; }
    }

    private static GameNetworkServer CreateServer(
        GameRoomService roomService,
        ActiveGameParticipantRegistry registry,
        Dictionary<string, string?>? overrides = null,
        IGameRecordQueue? queue = null)
    {
        var values = new Dictionary<string, string?>
            {
                ["GameNetwork:TcpPort"] = "0",
                ["GameNetwork:UdpPort"] = "0",
                ["Operations:DrainSeconds"] = "0"
            };
        if (overrides != null) foreach (var entry in overrides) values[entry.Key] = entry.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var liveKit = new LiveKitRoomAdminService(
            Options.Create(new LiveKitOptions { Url = "invalid-url" }),
            NullLogger<LiveKitRoomAdminService>.Instance);
        return new GameNetworkServer(
            roomService,
            registry,
            liveKit,
            queue ?? new NoopGameRecordQueue(),
            configuration,
            NullLogger<GameNetworkServer>.Instance);
    }

    private static async Task<TcpClient> ConnectAsync(int port)
    {
        var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static void WriteFrame(TcpClient client, TcpMessageType type, string payload)
    {
        using var writer = new BinaryWriter(client.GetStream(), Encoding.UTF8, leaveOpen: true);
        var byteCount = Encoding.UTF8.GetByteCount(payload);
        writer.Write(1 + SevenBitLength(byteCount) + byteCount);
        writer.Write((byte)type);
        writer.Write(payload);
        writer.Flush();
    }

    private static async Task<string> ReadUntilTypeAsync(TcpClient client, TcpMessageType wanted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var frameNumber = 0; frameNumber < 30; frameNumber++)
        {
            var (type, payload) = await ReadFrameAsync(client.GetStream(), timeout.Token);
            if (type == wanted) return payload;
        }
        throw new AssertionException($"Did not receive {wanted} within 30 TCP frames.");
    }

    private static async Task<(TcpMessageType Type, string Payload)> ReadFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var intBuffer = new byte[4];
        await stream.ReadExactlyAsync(intBuffer, cancellationToken);
        var declaredLength = BitConverter.ToInt32(intBuffer);
        Assert.That(declaredLength, Is.GreaterThan(0));

        var singleByte = new byte[1];
        await stream.ReadExactlyAsync(singleByte, cancellationToken);
        var type = (TcpMessageType)singleByte[0];
        var length = 0;
        var prefixBytes = 0;
        for (var index = 0; index < 5; index++)
        {
            await stream.ReadExactlyAsync(singleByte, cancellationToken);
            prefixBytes++;
            length |= (singleByte[0] & 0x7F) << (index * 7);
            if ((singleByte[0] & 0x80) == 0) break;
        }

        Assert.That(length, Is.InRange(0, 4096));
        var payloadBytes = new byte[length];
        await stream.ReadExactlyAsync(payloadBytes, cancellationToken);
        var payload = Encoding.UTF8.GetString(payloadBytes);
        Assert.That(declaredLength, Is.EqualTo(1 + prefixBytes + length),
            "Outbound TCP frames must declare the actual number of bytes sent.");
        return (type, payload);
    }

    private static async Task AssertRemoteClosedAsync(TcpClient client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[1];
        Assert.That(await client.GetStream().ReadAsync(buffer, timeout.Token), Is.Zero,
            "Rejected TCP input must close the socket without sending a game response.");
    }

    private static int SevenBitLength(int length)
    {
        var count = 1;
        while (length >= 128)
        {
            length >>= 7;
            count++;
        }
        return count;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
                throw new AssertionException($"Condition did not become true within {timeout}.");
            await Task.Delay(10);
        }
    }

    private static async Task<int> WaitForTcpPortAsync(GameNetworkServer server)
    {
        await WaitUntilAsync(() => GetTcpPort(server) > 0, TimeSpan.FromSeconds(3));
        return GetTcpPort(server);
    }

    private static int GetTcpPort(GameNetworkServer server) =>
        (((TcpListener)GetField(server, "_tcpListener")!).Server.LocalEndPoint as IPEndPoint)?.Port ?? 0;

    private static int GetTcpConnectionCount(GameNetworkServer server) =>
        (int)GetField(server, "_currentTcpConnections")!;

    private static ConcurrentDictionary<string, GameSession> GetGameSessions(GameNetworkServer server) =>
        (ConcurrentDictionary<string, GameSession>)GetField(server, "_gameSessions")!;

    private static object? GetField(GameNetworkServer server, string name) =>
        typeof(GameNetworkServer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server);

    private static async Task StopAndDisposeAsync(GameNetworkServer server)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await server.StopAsync(timeout.Token);
        }
        finally
        {
            server.Dispose();
        }
    }

    private static string CreateLoginSession(string userId) => (string)typeof(AuthController)
        .GetMethod("CreateSession", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [userId])!;

    private static void Logout(string token) =>
        new AuthController(null!, null!, null!).Logout(new AuthController.LogoutRequest(token));

    private sealed class NoopGameRecordQueue : IGameRecordQueue
    {
        public bool TryEnqueue(CompletedGameRecord record) => true;
    }
}
