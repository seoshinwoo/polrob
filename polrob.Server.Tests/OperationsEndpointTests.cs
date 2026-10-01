using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using polrob.Server.Operations;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class OperationsEndpointTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _directory = null!;

    [SetUp]
    public async Task Start()
    {
        _directory = Path.Combine(Path.GetTempPath(), "polrob-ops-tests-" + Guid.NewGuid().ToString("N"));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Operations:ApiKey"] = "operations-test-key",
            ["Limits:HttpRequestsPerMinute"] = "2",
            ["Limits:AuthRequestsPerMinute"] = "1",
            ["GameNetwork:MaxRooms"] = "1"
        });
        builder.Services.Configure<GameRecordOutboxOptions>(options =>
        {
            options.Directory = _directory;
            options.MaxRecords = 2;
            options.AdmissionThreshold = 0.5;
        });
        builder.Services.AddSingleton<GameRecordOutbox>();
        builder.Services.AddSingleton<OperationalMetrics>();
        builder.Services.AddSingleton<ServerOperations>();
        builder.Services.AddSingleton<ServerAdmission>();
        builder.Services.AddSingleton<GameRecordWriter>();
        builder.Services.AddSingleton<IGameRecordStore, FakeStore>();
        builder.Services.AddRequestLimits(builder.Configuration);
        _app = builder.Build();
        _app.UseRouting();
        _app.UseRateLimiter();
        _app.MapOperations();
        _app.MapGet("/api/ping", () => "pong");
        _app.MapPost("/auth/login", () => "login");
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    [TearDown]
    public async Task Stop()
    {
        _client?.Dispose();
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public async Task ReadinessChangesAcrossStartupDrainWhileLivenessStaysAvailable()
    {
        Assert.That((await _client.GetAsync("health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        _app.Services.GetRequiredService<ServerOperations>().MarkStarted();
        Assert.That((await _client.GetAsync("health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await _client.PostAsync("ops/drain", null)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        _client.DefaultRequestHeaders.Add("X-Operations-Key", "operations-test-key");
        Assert.That((await _client.PostAsync("ops/drain", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await _client.GetAsync("health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await _client.GetAsync("health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task MetricsRequireKeyAndContainBacklogWithoutPlayerIdentifiers()
    {
        var outbox = _app.Services.GetRequiredService<GameRecordOutbox>();
        outbox.TryAppend(new CompletedGameRecord("secret-game-id", "secret-room-id", PlayerRole.Police,
            new[] { "secret-player" }, new[] { "robber" }, DateTime.UtcNow.AddSeconds(-60), DateTime.UtcNow, 60));
        Assert.That((await _client.GetAsync("ops/metrics")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        _client.DefaultRequestHeaders.Add("X-Operations-Key", "operations-test-key");
        var text = await _client.GetStringAsync("ops/metrics");
        Assert.That(text, Does.Contain("polrob_outbox_pending 1\n"));
        Assert.That(text, Does.Not.Contain("secret-"));
        _app.Services.GetRequiredService<ServerOperations>().MarkStarted();
        Assert.That((await _client.GetAsync("health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task HttpAndLoginLimitsReturn429WithRetryAfterWithoutLimitingHealth()
    {
        Assert.That((await _client.GetAsync("api/ping")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await _client.GetAsync("api/ping")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rejected = await _client.GetAsync("api/ping");
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(rejected.Headers.RetryAfter, Is.Not.Null);
        Assert.That((await _client.PostAsync("auth/login", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await _client.PostAsync("auth/login", null)).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That((await _client.GetAsync("health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task RoomLimitAndDrainAreEnforcedInsideRoomService()
    {
        var identities = new BotIdentityService(Microsoft.Extensions.Logging.Abstractions.NullLogger<BotIdentityService>.Instance);
        var rooms = new GameRoomService(null!, identities,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GameRoomService>.Instance,
            admission: _app.Services.GetRequiredService<ServerAdmission>());
        var player = identities.Create("player", PlayerRole.Police);
        Assert.That((await rooms.CreateRoom(player.Id)).Success, Is.True);
        Assert.That((await rooms.CreateRoom(player.Id)).Success, Is.False);
        _app.Services.GetRequiredService<ServerOperations>().BeginDrain();
        Assert.That((await rooms.JoinRandomGame(player.Id, "", PlayerRole.Police)).Success, Is.False);
        Assert.That(rooms.GetLoadSnapshot().TotalRooms, Is.EqualTo(1));
    }

    private sealed class FakeStore : IGameRecordStore
    {
        public Task SaveGameRecordAsync(CompletedGameRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
