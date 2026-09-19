using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using polrob.Server.Controllers;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameRecordsControllerTests
{
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
    public async Task GetMyStats_WithoutAuthenticatedSession_ReturnsUnauthorized()
    {
        var statsReader = new RecordingGameRecordStatsReader();
        var controller = CreateController(statsReader);

        var result = await controller.GetMyStats(CancellationToken.None);

        var unauthorized = result.Result as UnauthorizedObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(unauthorized, Is.Not.Null);
            Assert.That(unauthorized?.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(statsReader.CallCount, Is.Zero);
        });
    }

    [Test]
    public async Task GetMyStats_WithValidSession_ReturnsStatsForAuthenticatedUser()
    {
        const string authenticatedUserId = "stats-controller-user";
        var expectedStats = new PlayerGameStats
        {
            Overall = new GameStatsBreakdown
            {
                TotalGames = 3,
                Wins = 2,
                Losses = 1,
                WinRate = 200d / 3d
            },
            Police = new GameStatsBreakdown
            {
                TotalGames = 1,
                Wins = 1,
                Losses = 0,
                WinRate = 100d
            },
            Robber = new GameStatsBreakdown
            {
                TotalGames = 2,
                Wins = 1,
                Losses = 1,
                WinRate = 50d
            }
        };
        var statsReader = new RecordingGameRecordStatsReader(expectedStats);
        var controller = CreateController(statsReader);
        Authenticate(controller, authenticatedUserId);
        using var cancellation = new CancellationTokenSource();

        var result = await controller.GetMyStats(cancellation.Token);

        var ok = result.Result as OkObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(ok?.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(ok?.Value, Is.SameAs(expectedStats));
            Assert.That(statsReader.CallCount, Is.EqualTo(1));
            Assert.That(statsReader.RequestedUserId, Is.EqualTo(authenticatedUserId));
            Assert.That(statsReader.CancellationToken, Is.EqualTo(cancellation.Token));
        });
    }

    private static GameRecordsController CreateController(IGameRecordStatsReader statsReader)
    {
        return new GameRecordsController(statsReader)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private void Authenticate(GameRecordsController controller, string userId)
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

    private sealed class RecordingGameRecordStatsReader : IGameRecordStatsReader
    {
        private readonly PlayerGameStats _stats;

        public RecordingGameRecordStatsReader(PlayerGameStats? stats = null)
        {
            _stats = stats ?? new PlayerGameStats();
        }

        public int CallCount { get; private set; }
        public string? RequestedUserId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PlayerGameStats> GetPlayerStatsAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedUserId = userId;
            CancellationToken = cancellationToken;
            return Task.FromResult(_stats);
        }
    }
}
