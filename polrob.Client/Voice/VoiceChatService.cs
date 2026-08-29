namespace polrob.Client.Voice;

public sealed class VoiceChatService : IAsyncDisposable
{
    private readonly VoiceTokenClient _tokenClient;
    private readonly IVoiceRoomClient _roomClient;

    public VoiceChatService(VoiceTokenClient tokenClient, IVoiceRoomClient roomClient)
    {
        _tokenClient = tokenClient;
        _roomClient = roomClient;
    }

    public bool IsConnected => _roomClient.IsConnected;
    public bool IsLocalMicrophoneMuted => _roomClient.IsLocalMicrophoneMuted;
    public IReadOnlyList<VoiceParticipantState> Participants => _roomClient.Participants;

    public event EventHandler? ParticipantsChanged
    {
        add => _roomClient.ParticipantsChanged += value;
        remove => _roomClient.ParticipantsChanged -= value;
    }

    public event EventHandler<VoiceConnectionStateChangedEventArgs>? ConnectionStateChanged
    {
        add => _roomClient.ConnectionStateChanged += value;
        remove => _roomClient.ConnectionStateChanged -= value;
    }

    public async Task JoinTeamVoiceAsync(
        string roomId,
        CancellationToken cancellationToken = default)
    {
        // API key/secret 대신 서버가 발급한 짧은 참가자 토큰만 LiveKit 클라이언트에 전달합니다.
        var connectionInfo = await _tokenClient.GetConnectionInfoAsync(roomId, cancellationToken);
        await _roomClient.ConnectAsync(connectionInfo, cancellationToken);
    }

    public Task SetLocalMicrophoneMutedAsync(
        bool muted,
        CancellationToken cancellationToken = default)
    {
        return _roomClient.SetLocalMicrophoneMutedAsync(muted, cancellationToken);
    }

    public Task SetRemotePlaybackMutedAsync(
        string participantIdentity,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        return _roomClient.SetRemotePlaybackMutedAsync(
            participantIdentity,
            muted,
            cancellationToken);
    }

    public Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        return _roomClient.DisconnectAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _roomClient.DisconnectAsync();
        await _roomClient.DisposeAsync();
    }
}
