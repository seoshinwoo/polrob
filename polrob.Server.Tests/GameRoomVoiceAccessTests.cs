using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRoomVoiceAccessTests
{
    [Test]
    public void TeamVoiceAccess_IsAvailableOnlyWhileGameIsActive()
    {
        var service = CreateServiceWithGame(out var game);

        Assert.That(
            service.TryGetAuthenticatedTeamVoiceAccess(
                game.Id,
                "police-1",
                out _,
                out _),
            Is.False);

        var start = service.StartGameIfMatched(game.Id);
        var granted = service.TryGetAuthenticatedTeamVoiceAccess(
            game.Id,
            "police-1",
            out var player,
            out var voiceSessionId);

        Assert.Multiple(() =>
        {
            Assert.That(start.Success, Is.True);
            Assert.That(granted, Is.True);
            Assert.That(player?.Role, Is.EqualTo(PlayerRole.Police));
            Assert.That(voiceSessionId, Is.Not.Empty);
        });

        service.CompleteGame(game.Id);

        Assert.That(
            service.TryGetAuthenticatedTeamVoiceAccess(
                game.Id,
                "police-1",
                out _,
                out _),
            Is.False);
    }

    [Test]
    public void StartGame_CreatesOneVoiceSessionPerMatchAndRotatesForReplay()
    {
        var service = CreateServiceWithGame(out var game);

        service.StartGameIfMatched(game.Id);
        var firstSessionId = game.VoiceSessionId;

        service.StartGameIfMatched(game.Id);
        var repeatedStartSessionId = game.VoiceSessionId;

        service.CompleteGame(game.Id);
        service.StartGameIfMatched(game.Id);
        var replaySessionId = game.VoiceSessionId;

        Assert.Multiple(() =>
        {
            Assert.That(firstSessionId, Is.Not.Empty);
            Assert.That(repeatedStartSessionId, Is.EqualTo(firstSessionId));
            Assert.That(replaySessionId, Is.Not.Empty.And.Not.EqualTo(firstSessionId));
        });
    }

    [Test]
    public async Task UndefinedRole_IsRejectedBeforeAnyUserLookup()
    {
        var service = new GameRoomService(
            userDbService: null!,
            botIdentityService: null!,
            NullLogger<GameRoomService>.Instance);

        var result = await service.CreateRoom("user-1", role: (PlayerRole)999);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("지원하지 않는"));
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
            Id = "room-voice-test",
            HostUserId = "police-1",
            Players =
            [
                CreatePlayer("police-1", PlayerRole.Police, "room-voice-test"),
                CreatePlayer("robber-1", PlayerRole.Robber, "room-voice-test")
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

    private static Player CreatePlayer(string id, PlayerRole role, string roomId) => new()
    {
        Id = id,
        Name = id,
        RoomId = roomId,
        Role = role
    };
}
