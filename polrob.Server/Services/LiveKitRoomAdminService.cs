using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Options;
using polrob.Shared;

public sealed class LiveKitRoomAdminService
{
    private readonly LiveKitOptions _options;
    private readonly ILogger<LiveKitRoomAdminService> _logger;

    public LiveKitRoomAdminService(
        IOptions<LiveKitOptions> options,
        ILogger<LiveKitRoomAdminService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task DeleteTeamRoomsAsync(string roomId, string voiceSessionId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(voiceSessionId))
        {
            return;
        }

        var client = CreateClient();

        foreach (var role in Enum.GetValues<PlayerRole>())
        {
            var roomName = LiveKitTokenService.CreateTeamRoomName(
                roomId,
                voiceSessionId,
                role);
            try
            {
                await client.DeleteRoom(new DeleteRoomRequest { Room = roomName });
            }
            catch (Exception exception)
            {
                // 방이 만들어지지 않았거나 이미 사라졌어도 게임 종료 처리는 성공해야 합니다.
                _logger.LogWarning(
                    exception,
                    "Failed to delete LiveKit team room {RoomName}.",
                    roomName);
            }
        }
    }

    public async Task RemoveParticipantAsync(
        string roomId,
        string voiceSessionId,
        PlayerRole role,
        string userId,
        string gameConnectionId)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameConnectionId))
        {
            return;
        }

        var roomName = LiveKitTokenService.CreateTeamRoomName(roomId, voiceSessionId, role);
        var participantIdentity = LiveKitTokenService.CreateParticipantIdentity(
            roomId,
            userId,
            gameConnectionId);
        try
        {
            await CreateClient().RemoveParticipant(new RoomParticipantIdentity
            {
                Room = roomName,
                Identity = participantIdentity
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to remove participant {ParticipantIdentity} from LiveKit room {RoomName}.",
                participantIdentity,
                roomName);
        }
    }

    private RoomServiceClient CreateClient()
    {
        var webSocketUri = new Uri(_options.Url);
        var serviceUri = new UriBuilder(webSocketUri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = webSocketUri.IsDefaultPort ? -1 : webSocketUri.Port
        }.Uri;
        return new RoomServiceClient(
            serviceUri.ToString().TrimEnd('/'),
            _options.ApiKey,
            _options.ApiSecret);
    }
}
