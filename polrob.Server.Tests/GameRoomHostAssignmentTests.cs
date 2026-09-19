using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRoomHostAssignmentTests
{
    [Test]
    public void RemovePlayer_WhenNonHostLeaves_KeepsExistingHost()
    {
        var service = CreateServiceWithGame(out var game);

        var status = service.RemovePlayer(game.Id, "player-2");

        Assert.Multiple(() =>
        {
            Assert.That(status.HostUserId, Is.EqualTo("host-1"));
            Assert.That(game.HostUserId, Is.EqualTo("host-1"));
        });
    }

    [Test]
    public void RemovePlayer_WhenHostLeaves_AssignsFirstRemainingPlayer()
    {
        var service = CreateServiceWithGame(out var game);

        var status = service.RemovePlayer(game.Id, "host-1");

        Assert.Multiple(() =>
        {
            Assert.That(status.HostUserId, Is.EqualTo("player-2"));
            Assert.That(game.HostUserId, Is.EqualTo("player-2"));
        });
    }

    [Test]
    public void CompleteGame_ForCustomRoom_PreservesPlayersAndHostForReplay()
    {
        var service = CreateServiceWithGame(out var game);
        service.StartGameIfMatched(game.Id);

        var status = service.CompleteGame(game.Id);

        Assert.Multiple(() =>
        {
            Assert.That(status.Success, Is.True);
            Assert.That(status.HostUserId, Is.EqualTo("host-1"));
            Assert.That(status.Players.Select(player => player.Id),
                Is.EqualTo(new[] { "host-1", "player-2", "player-3" }));
            Assert.That(game.IsOnGame, Is.False);
        });
    }

    [Test]
    public void CompleteGame_ForRandomRoom_KeepsExistingCleanupBehavior()
    {
        var service = CreateServiceWithGame(out var game);
        game.Type = "random";
        game.IsPrivate = false;
        service.StartGameIfMatched(game.Id);

        var completion = service.CompleteGame(game.Id);
        var statusAfterCompletion = service.GetRoomStatus(game.Id);

        Assert.Multiple(() =>
        {
            Assert.That(completion.Success, Is.True);
            Assert.That(completion.Matched, Is.False);
            Assert.That(statusAfterCompletion.Success, Is.False);
        });
    }

    private static GameRoomService CreateServiceWithGame(out Game game)
    {
        var service = new GameRoomService(
            userDbService: null!,
            botIdentityService: null!,
            NullLogger<GameRoomService>.Instance);
        game = new Game("custom", isPrivate: true)
        {
            Id = "room-host-test",
            HostUserId = "host-1",
            Players =
            [
                CreatePlayer("host-1", PlayerRole.Police),
                CreatePlayer("player-2", PlayerRole.Robber),
                CreatePlayer("player-3", PlayerRole.Robber)
            ]
        };

        var gamesField = typeof(GameRoomService).GetField(
            "Games",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("GameRoomService.Games field was not found.");
        var games = (List<Game>?)gamesField.GetValue(service)
            ?? throw new InvalidOperationException("GameRoomService.Games was not initialized.");
        games.Add(game);
        return service;
    }

    private static Player CreatePlayer(string id, PlayerRole role) => new()
    {
        Id = id,
        Name = id,
        RoomId = "room-host-test",
        Role = role
    };
}
