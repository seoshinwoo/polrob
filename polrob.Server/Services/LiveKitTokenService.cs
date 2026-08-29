using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Options;
using polrob.Shared;

public sealed class LiveKitTokenService
{
    private readonly LiveKitOptions _options;

    public LiveKitTokenService(IOptions<LiveKitOptions> options)
    {
        _options = options.Value;
    }

    public VoiceConnectionInfo CreateTeamVoiceToken(Player player)
    {
        ValidateConfiguration();

        // 방 ID와 서버가 확인한 역할을 함께 사용하므로 상대 팀 채널의 토큰을 요청할 수 없습니다.
        var roomName = CreateTeamRoomName(player.RoomId, player.Role);
        var lifetimeMinutes = Math.Clamp(_options.TokenLifetimeMinutes, 1, 60);

        var participantToken = new AccessToken(_options.ApiKey, _options.ApiSecret)
            .WithIdentity(player.Id)
            .WithName(player.Name)
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
            (uri.Scheme != "wss" && uri.Scheme != "ws"))
        {
            throw new InvalidOperationException(
                "LiveKit:Url이 설정되지 않았습니다. 로컬은 user-secrets, Azure는 App Settings/Key Vault에 WebSocket URL을 설정하세요.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ApiSecret))
        {
            throw new InvalidOperationException(
                "LiveKit API key 또는 API secret이 설정되지 않았습니다. TODO(LIVEKIT_CREDENTIALS) 주석을 확인하세요.");
        }
    }

    private static string CreateTeamRoomName(string roomId, PlayerRole role)
    {
        var team = role == PlayerRole.Police ? "police" : "robber";
        return $"polrob-{roomId}-{team}";
    }
}
