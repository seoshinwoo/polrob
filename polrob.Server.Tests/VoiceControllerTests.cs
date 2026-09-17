using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using polrob.Server.Controllers;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class VoiceControllerTests
{
    private const string UserId = "voice-controller-user";
    private const string RoomId = "voice-controller-room";
    private readonly List<string> _sessionTokens = [];

    [TearDown]
    public void RemoveCreatedSessions()
    {
        var authController = new AuthController(null!, null!, null!);
        foreach (var sessionToken in _sessionTokens)
        {
            authController.Logout(new AuthController.LogoutRequest(sessionToken));
        }

        _sessionTokens.Clear();
    }

    [Test]
    public void CreateToken_WithoutAuthenticatedSession_ReturnsUnauthorized()
    {
        var controller = CreateController(CreateRoomService(out _), new ActiveGameParticipantRegistry());

        var result = controller.CreateToken(new VoiceTokenRequest(RoomId));

        var unauthorized = result.Result as UnauthorizedObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(unauthorized, Is.Not.Null);
            Assert.That(unauthorized?.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateToken_WithMissingRoomId_ReturnsBadRequest(string roomId)
    {
        var controller = CreateController(CreateRoomService(out _), new ActiveGameParticipantRegistry());
        Authenticate(controller, UserId);

        var result = controller.CreateToken(new VoiceTokenRequest(roomId));

        var badRequest = result.Result as BadRequestObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(badRequest, Is.Not.Null);
            Assert.That(badRequest?.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        });
    }

    [TestCase(false, true, TestName = "CreateToken_WhenGameIsInactive_ReturnsPlain403")]
    [TestCase(true, false, TestName = "CreateToken_WhenParticipantIsNotActive_ReturnsPlain403")]
    public void CreateToken_WithoutActiveGameConnection_ReturnsPlain403(
        bool startGame,
        bool registerParticipant)
    {
        var roomService = CreateRoomService(out var game);
        var registry = new ActiveGameParticipantRegistry();
        if (startGame)
        {
            Assert.That(roomService.StartGameIfMatched(game.Id).Success, Is.True);
        }

        if (registerParticipant)
        {
            registry.Register(game.Id, UserId, "tcp-connection-1");
        }

        var controller = CreateController(roomService, registry);
        Authenticate(controller, UserId);

        var result = controller.CreateToken(new VoiceTokenRequest(game.Id));

        Assert.Multiple(() =>
        {
            Assert.That(result.Result, Is.TypeOf<StatusCodeResult>());
            Assert.That(result.Result, Is.Not.InstanceOf<ForbidResult>());
            Assert.That(
                ((StatusCodeResult?)result.Result)?.StatusCode,
                Is.EqualTo(StatusCodes.Status403Forbidden));
        });
    }

    [Test]
    public void CreateToken_ForActiveParticipant_IssuesTeamVoiceConnectionInfo()
    {
        var roomService = CreateRoomService(out var game);
        Assert.That(roomService.StartGameIfMatched(game.Id).Success, Is.True);

        var registry = new ActiveGameParticipantRegistry();
        registry.Register(game.Id, UserId, "tcp-connection-1");
        var controller = CreateController(roomService, registry);
        Authenticate(controller, UserId);

        var result = controller.CreateToken(new VoiceTokenRequest(game.Id));

        var ok = result.Result as OkObjectResult;
        var connectionInfo = ok?.Value as VoiceConnectionInfo;
        Assert.Multiple(() =>
        {
            Assert.That(ok?.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(connectionInfo, Is.Not.Null);
            Assert.That(connectionInfo?.ServerUrl, Is.EqualTo("wss://voice.example.test"));
            Assert.That(connectionInfo?.ParticipantToken, Is.Not.Empty);
            Assert.That(connectionInfo?.Role, Is.EqualTo(PlayerRole.Police));
            Assert.That(
                connectionInfo?.RoomName,
                Is.EqualTo($"polrob-{game.Id}-{game.VoiceSessionId}-police"));
            Assert.That(connectionInfo?.ExpiresAtUtc, Is.GreaterThan(DateTime.UtcNow));
        });
    }

    private static VoiceController CreateController(
        GameRoomService roomService,
        ActiveGameParticipantRegistry registry)
    {
        var tokenService = new LiveKitTokenService(Options.Create(new LiveKitOptions
        {
            Url = "wss://voice.example.test",
            ApiKey = "test-api-key",
            ApiSecret = "test-api-secret-that-is-long-enough-for-hmac",
            TokenLifetimeMinutes = 15
        }));
        return new VoiceController(
            roomService,
            tokenService,
            registry,
            NullLogger<VoiceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private void Authenticate(VoiceController controller, string userId)
    {
        var createSession = typeof(AuthController).GetMethod(
            "CreateSession",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AuthController.CreateSession was not found.");
        var sessionToken = (string?)createSession.Invoke(null, [userId])
            ?? throw new InvalidOperationException("AuthController.CreateSession returned no token.");

        _sessionTokens.Add(sessionToken);
        controller.Request.Headers.Authorization = $"Bearer {sessionToken}";
    }

    private static GameRoomService CreateRoomService(out Game game)
    {
        var service = new GameRoomService(
            userDbService: null!,
            botIdentityService: null!,
            NullLogger<GameRoomService>.Instance);
        game = new Game("custom", isPrivate: true)
        {
            Id = RoomId,
            HostUserId = UserId,
            Players =
            [
                CreatePlayer(UserId, PlayerRole.Police),
                CreatePlayer("voice-controller-robber", PlayerRole.Robber)
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
        RoomId = RoomId,
        Role = role
    };
}
