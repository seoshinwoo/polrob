using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRoomLifecycleTests
{
    [Test]
    public async Task CompletingOneRoomDoesNotChangeAnotherRoomsPlayersOrVoiceSession()
    {
        var (service, identities) = CreateServices();
        var first = await CreateStartedRoom(service, identities);
        var second = await CreateStartedRoom(service, identities);

        Assert.That(service.GetAuthenticatedGamePlayer(first.RoomId, second.HostId), Is.Null);
        Assert.That(service.GetAuthenticatedGamePlayer(second.RoomId, first.HostId), Is.Null);
        Assert.That(service.TryGetAuthenticatedTeamVoiceAccess(
            second.RoomId, second.HostId, out _, out var secondVoiceSessionId), Is.True);

        service.RemovePlayer(first.RoomId, first.RobberId);
        var firstCompletion = service.CompleteGame(first.RoomId);
        var firstStatus = service.GetRoomStatus(first.RoomId);
        var secondStatus = service.GetRoomStatus(second.RoomId);

        Assert.Multiple(() =>
        {
            Assert.That(firstCompletion.Success, Is.True);
            Assert.That(firstStatus.Players.Select(player => player.Id),
                Is.EqualTo(new[] { first.HostId }));
            Assert.That(service.IsGameInProgress(first.RoomId), Is.False);
            Assert.That(secondStatus.Players.Select(player => player.Id),
                Is.EquivalentTo(new[] { second.HostId, second.RobberId }));
            Assert.That(service.IsGameInProgress(second.RoomId), Is.True);
            Assert.That(service.TryGetAuthenticatedTeamVoiceAccess(
                second.RoomId, second.HostId, out _, out var currentVoiceSessionId), Is.True);
            Assert.That(currentVoiceSessionId, Is.EqualTo(secondVoiceSessionId));
        });
    }

    [Test]
    public async Task ConcurrentDuplicateJoinsKeepOnePlayerAndOriginalRole()
    {
        var (service, identities) = CreateServices();
        var host = identities.Create("host", PlayerRole.Police);
        var robber = identities.Create("robber", PlayerRole.Robber);
        var room = await service.CreateRoom(host.Id);
        var firstJoin = await service.JoinCustomGame(robber.Id, room.RoomCode!);
        Assert.That(firstJoin.Success, Is.True);

        var duplicateJoins = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => service.JoinCustomGame(
                robber.Id, room.RoomCode!, PlayerRole.Police))));
        var status = service.GetRoomStatus(room.RoomId!);

        Assert.Multiple(() =>
        {
            Assert.That(duplicateJoins.All(response => response.Success), Is.True);
            Assert.That(status.CurrentCount, Is.EqualTo(2));
            Assert.That(status.Players.Count(player => player.Id == robber.Id), Is.EqualTo(1));
            Assert.That(service.GetAuthenticatedGamePlayer(room.RoomId!, robber.Id)?.Role,
                Is.EqualTo(PlayerRole.Robber));
        });
    }

    [Test]
    public void LateDisconnectFromReplacedConnectionDoesNotAffectOtherRoomOrNewConnection()
    {
        var registry = new ActiveGameParticipantRegistry();
        registry.Register("first-room", "user", "old-connection");
        registry.Register("first-room", "user", "new-connection");
        registry.Register("second-room", "user", "second-room-connection");

        registry.Unregister("first-room", "user", "old-connection");
        Assert.That(registry.TryGetConnectionId(
            "first-room", "user", out var firstConnection), Is.True);
        Assert.That(firstConnection, Is.EqualTo("new-connection"));

        registry.Unregister("first-room", "user", "new-connection");
        Assert.Multiple(() =>
        {
            Assert.That(registry.IsActive("first-room", "user"), Is.False);
            Assert.That(registry.TryGetConnectionId(
                "second-room", "user", out var secondConnection), Is.True);
            Assert.That(secondConnection, Is.EqualTo("second-room-connection"));
        });
    }

    [Test]
    public async Task LastDisconnectBeforeCompletionRemovesEmptyCustomRoom()
    {
        var (service, identities) = CreateServices();
        var room = await CreateStartedRoom(service, identities);

        service.RemovePlayer(room.RoomId, room.HostId);
        service.RemovePlayer(room.RoomId, room.RobberId);
        Assert.That(service.GetRoomStatus(room.RoomId).Success, Is.True,
            "An active game retains its room until completion is processed.");

        var completion = service.CompleteGame(room.RoomId);

        Assert.Multiple(() =>
        {
            Assert.That(completion.Success, Is.True);
            Assert.That(service.GetRoomStatus(room.RoomId).Success, Is.False);
            Assert.That(service.GetLoadSnapshot().TotalRooms, Is.Zero);
        });
    }

    [Test]
    public async Task RepeatedCompletionAndDisconnectInterleavingsLeaveNoRooms()
    {
        const int iterations = 200;
        var (service, identities) = CreateServices();
        var stopwatch = Stopwatch.StartNew();

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var room = await CreateStartedRoom(service, identities);
            switch (iteration % 3)
            {
                case 0:
                    service.RemovePlayer(room.RoomId, room.HostId);
                    service.RemovePlayer(room.RoomId, room.RobberId);
                    service.CompleteGame(room.RoomId);
                    break;
                case 1:
                    service.CompleteGame(room.RoomId);
                    service.RemovePlayer(room.RoomId, room.HostId);
                    service.RemovePlayer(room.RoomId, room.RobberId);
                    break;
                default:
                    await Task.WhenAll(
                        Task.Run(() => service.CompleteGame(room.RoomId)),
                        Task.Run(() =>
                        {
                            service.RemovePlayer(room.RoomId, room.HostId);
                            service.RemovePlayer(room.RoomId, room.RobberId);
                        }));
                    break;
            }

            Assert.That(service.GetRoomStatus(room.RoomId).Success, Is.False,
                $"Room leaked on iteration {iteration}.");
        }

        stopwatch.Stop();
        TestContext.Progress.WriteLine(
            $"Lifecycle stress: {iterations} rooms, 400 players, three completion/disconnect orderings, " +
            $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.That(service.GetLoadSnapshot().TotalRooms, Is.Zero);
    }

    private static (GameRoomService Service, BotIdentityService Identities) CreateServices()
    {
        var identities = new BotIdentityService(NullLogger<BotIdentityService>.Instance);
        var service = new GameRoomService(
            userDbService: null!, identities, NullLogger<GameRoomService>.Instance);
        return (service, identities);
    }

    private static async Task<(string RoomId, string HostId, string RobberId)> CreateStartedRoom(
        GameRoomService service,
        BotIdentityService identities)
    {
        var host = identities.Create("host", PlayerRole.Police);
        var robber = identities.Create("robber", PlayerRole.Robber);
        var created = await service.CreateRoom(host.Id);
        Assert.That(created.Success, Is.True, created.Message);
        var joined = await service.JoinCustomGame(robber.Id, created.RoomCode!);
        Assert.That(joined.Success, Is.True, joined.Message);
        var started = service.StartGameIfMatched(created.RoomId!);
        Assert.That(started.Success, Is.True, started.Message);
        Assert.That(service.IsGameInProgress(created.RoomId!), Is.True);
        return (created.RoomId!, host.Id, robber.Id);
    }
}
