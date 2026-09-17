using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Options;
using polrob.Shared;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class LiveKitTokenService
{
    private readonly LiveKitOptions _options;

    public LiveKitTokenService(IOptions<LiveKitOptions> options)
    {
        _options = options.Value;
    }

    public VoiceConnectionInfo CreateTeamVoiceToken(
        Player player,
        string voiceSessionId,
        string gameConnectionId)
    {
        ValidateConfiguration();
        if (string.IsNullOrWhiteSpace(voiceSessionId))
        {
            throw new ArgumentException("보이스 세션 ID가 필요합니다.", nameof(voiceSessionId));
        }
        if (string.IsNullOrWhiteSpace(gameConnectionId))
        {
            throw new ArgumentException("게임 연결 ID가 필요합니다.", nameof(gameConnectionId));
        }

        // 매치별 세션 ID와 서버가 확인한 역할을 함께 사용하므로 이전 경기나 상대 팀 토큰을 재사용할 수 없습니다.
        var roomName = CreateTeamRoomName(player.RoomId, voiceSessionId, player.Role);
        var participantIdentity = CreateParticipantIdentity(
            player.RoomId,
            player.Id,
            gameConnectionId);
        // 연결이 끊긴 사용자가 캐시한 JWT로 오래 재입장하지 못하게 제한합니다.
        // 정상적인 장시간 연결은 유지되며, 완전 재접속 시 서버에서 새 토큰을 받습니다.
        var lifetimeMinutes = Math.Clamp(_options.TokenLifetimeMinutes, 1, 5);

        var participantToken = new AccessToken(_options.ApiKey, _options.ApiSecret)
            // 동일 사용자의 이전 TCP 연결 제거가 새 연결까지 끊지 않도록
            // LiveKit identity는 게임 연결 세대마다 다르게 발급합니다.
            .WithIdentity(participantIdentity)
            .WithName(player.Name)
            .WithMetadata(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["gameUserId"] = player.Id
            }))
            .WithTtl(TimeSpan.FromMinutes(lifetimeMinutes))
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = roomName,
                CanSubscribe = true,
                CanPublish = true,
                CanPublishData = false,
                // 팀 보이스 기능에는 카메라나 화면 공유가 필요하지 않으므로 마이크만 허용합니다.
                CanPublishSources = ["microphone"]
            })
            .ToJwt();

        return new VoiceConnectionInfo(
            ServerUrl: _options.Url,
            ParticipantToken: participantToken,
            RoomName: roomName,
            Role: player.Role,
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(lifetimeMinutes));
    }

    private void ValidateConfiguration()
    {
        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme != "wss")
        {
            throw new InvalidOperationException(
                "LiveKit:Url에 암호화된 wss:// WebSocket URL을 설정하세요. 로컬은 user-secrets, Azure는 App Settings/Key Vault를 사용합니다.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ApiSecret))
        {
            throw new InvalidOperationException(
                "LiveKit API key 또는 API secret이 설정되지 않았습니다. TODO(LIVEKIT_CREDENTIALS) 주석을 확인하세요.");
        }
    }

    internal static string CreateTeamRoomName(
        string roomId,
        string voiceSessionId,
        PlayerRole role)
    {
        var team = role switch
        {
            PlayerRole.Police => "police",
            PlayerRole.Robber => "robber",
            _ => throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "지원하지 않는 플레이어 역할입니다.")
        };
        return $"polrob-{roomId}-{voiceSessionId}-{team}";
    }

    internal static string CreateParticipantIdentity(
        string roomId,
        string userId,
        string gameConnectionId)
    {
        var identitySource = $"{roomId}\n{userId}\n{gameConnectionId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identitySource));
        return $"player-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
