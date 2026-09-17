using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class LiveKitTokenServiceTests
{
    private const string ServerUrl = "wss://voice.example.test";
    private const string ApiKey = "test-api-key";
    private const string ApiSecret = "test-api-secret-that-is-long-enough-for-hmac";
    private const string VoiceSessionId = "voice-session-7";
    private const string GameConnectionId = "tcp-connection-3";

    [TestCase(PlayerRole.Police, "polrob-room-42-voice-session-7-police")]
    [TestCase(PlayerRole.Robber, "polrob-room-42-voice-session-7-robber")]
    public void CreateTeamVoiceToken_UsesRoleSpecificRoom(
        PlayerRole role,
        string expectedRoomName)
    {
        var service = CreateService();
        var player = CreatePlayer(role);

        var result = service.CreateTeamVoiceToken(player, VoiceSessionId, GameConnectionId);
        using var payload = ReadPayload(result.ParticipantToken);
        var videoGrant = payload.RootElement.GetProperty("video");

        Assert.Multiple(() =>
        {
            Assert.That(result.ServerUrl, Is.EqualTo(ServerUrl));
            Assert.That(result.RoomName, Is.EqualTo(expectedRoomName));
            Assert.That(result.Role, Is.EqualTo(role));
            Assert.That(videoGrant.GetProperty("room").GetString(), Is.EqualTo(expectedRoomName));
        });
    }

    [Test]
    public void CreateTeamVoiceToken_EmitsParticipantClaimsAndMicrophoneOnlyGrants()
    {
        var service = CreateService();
        var player = CreatePlayer(PlayerRole.Police);

        var result = service.CreateTeamVoiceToken(player, VoiceSessionId, GameConnectionId);
        using var payload = ReadPayload(result.ParticipantToken);
        var root = payload.RootElement;
        using var metadata = JsonDocument.Parse(root.GetProperty("metadata").GetString()!);
        var videoGrant = root.GetProperty("video");
        var publishSources = videoGrant
            .GetProperty("canPublishSources")
            .EnumerateArray()
            .Select(source => source.GetString())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("iss").GetString(), Is.EqualTo(ApiKey));
            Assert.That(
                root.GetProperty("sub").GetString(),
                Does.StartWith("player-").And.Not.EqualTo(player.Id));
            Assert.That(root.GetProperty("name").GetString(), Is.EqualTo(player.Name));
            Assert.That(
                metadata.RootElement.GetProperty("gameUserId").GetString(),
                Is.EqualTo(player.Id));
            Assert.That(videoGrant.GetProperty("roomJoin").GetBoolean(), Is.True);
            Assert.That(videoGrant.GetProperty("canSubscribe").GetBoolean(), Is.True);
            Assert.That(videoGrant.GetProperty("canPublish").GetBoolean(), Is.True);
            Assert.That(videoGrant.GetProperty("canPublishData").GetBoolean(), Is.False);
            Assert.That(publishSources, Is.EqualTo(new[] { "microphone" }));
        });
    }

    [Test]
    public void CreateTeamVoiceToken_UsesConnectionSpecificParticipantIdentity()
    {
        var service = CreateService();
        var player = CreatePlayer(PlayerRole.Police);

        var first = service.CreateTeamVoiceToken(player, VoiceSessionId, "connection-a");
        var second = service.CreateTeamVoiceToken(player, VoiceSessionId, "connection-b");
        using var firstPayload = ReadPayload(first.ParticipantToken);
        using var secondPayload = ReadPayload(second.ParticipantToken);

        Assert.That(
            firstPayload.RootElement.GetProperty("sub").GetString(),
            Is.Not.EqualTo(secondPayload.RootElement.GetProperty("sub").GetString()));
    }

    [TestCase(0, 1)]
    [TestCase(2, 2)]
    [TestCase(15, 5)]
    public void CreateTeamVoiceToken_ClampsAndAppliesConfiguredTtl(
        int configuredMinutes,
        int expectedMinutes)
    {
        var service = CreateService(configuredMinutes);
        var beforeCreation = DateTime.UtcNow;

        var result = service.CreateTeamVoiceToken(
            CreatePlayer(PlayerRole.Robber),
            VoiceSessionId,
            GameConnectionId);

        var afterCreation = DateTime.UtcNow;
        using var payload = ReadPayload(result.ParticipantToken);
        var issuedAtSeconds = payload.RootElement.GetProperty("iat").GetInt64();
        var expiresAtSeconds = payload.RootElement.GetProperty("exp").GetInt64();

        Assert.Multiple(() =>
        {
            Assert.That(
                expiresAtSeconds - issuedAtSeconds,
                Is.EqualTo(TimeSpan.FromMinutes(expectedMinutes).TotalSeconds));
            Assert.That(
                result.ExpiresAtUtc,
                Is.InRange(
                    beforeCreation.AddMinutes(expectedMinutes),
                    afterCreation.AddMinutes(expectedMinutes)));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-a-url")]
    [TestCase("https://voice.example.test")]
    [TestCase("ws://voice.example.test")]
    public void CreateTeamVoiceToken_WithInvalidWebSocketUrl_Throws(string url)
    {
        var service = CreateService(url: url);

        Assert.That(
            () => service.CreateTeamVoiceToken(
                CreatePlayer(PlayerRole.Police),
                VoiceSessionId,
                GameConnectionId),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("LiveKit:Url"));
    }

    [TestCase("", ApiSecret)]
    [TestCase("   ", ApiSecret)]
    [TestCase(ApiKey, "")]
    [TestCase(ApiKey, "   ")]
    public void CreateTeamVoiceToken_WithMissingCredential_Throws(
        string apiKey,
        string apiSecret)
    {
        var service = CreateService(apiKey: apiKey, apiSecret: apiSecret);

        Assert.That(
            () => service.CreateTeamVoiceToken(
                CreatePlayer(PlayerRole.Robber),
                VoiceSessionId,
                GameConnectionId),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("API key 또는 API secret"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateTeamVoiceToken_WithMissingVoiceSessionId_Throws(string voiceSessionId)
    {
        var service = CreateService();

        Assert.That(
            () => service.CreateTeamVoiceToken(
                CreatePlayer(PlayerRole.Police),
                voiceSessionId,
                GameConnectionId),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains("보이스 세션 ID"));
    }

    [Test]
    public void CreateTeamVoiceToken_WithUndefinedRole_Throws()
    {
        var service = CreateService();
        var player = CreatePlayer((PlayerRole)999);

        Assert.That(
            () => service.CreateTeamVoiceToken(player, VoiceSessionId, GameConnectionId),
            Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Message.Contains("지원하지 않는 플레이어 역할"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateTeamVoiceToken_WithMissingGameConnectionId_Throws(string connectionId)
    {
        var service = CreateService();

        Assert.That(
            () => service.CreateTeamVoiceToken(
                CreatePlayer(PlayerRole.Police),
                VoiceSessionId,
                connectionId),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains("게임 연결 ID"));
    }

    private static LiveKitTokenService CreateService(
        int tokenLifetimeMinutes = 15,
        string url = ServerUrl,
        string apiKey = ApiKey,
        string apiSecret = ApiSecret)
    {
        var options = Options.Create(new LiveKitOptions
        {
            Url = url,
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            TokenLifetimeMinutes = tokenLifetimeMinutes
        });

        return new LiveKitTokenService(options);
    }

    private static Player CreatePlayer(PlayerRole role) => new()
    {
        Id = "player-17",
        Name = "Voice Tester",
        RoomId = "room-42",
        Role = role
    };

    private static JsonDocument ReadPayload(string token)
    {
        var tokenParts = token.Split('.');
        Assert.That(tokenParts, Has.Length.EqualTo(3), "Expected a three-part JWT.");

        var payload = tokenParts[1]
            .Replace('-', '+')
            .Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }
}
