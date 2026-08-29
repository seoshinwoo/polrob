using polrob.Shared;

namespace polrob.Client.Voice;

// GamePlay은 LiveKit SDK 타입을 직접 참조하지 않고 이 인터페이스만 사용합니다.
// 나중에 네이티브 Android/iOS 어댑터로 교체해도 게임 화면 코드는 바뀌지 않습니다.
public interface IVoiceRoomClient : IAsyncDisposable
{
    bool IsConnected { get; }
    bool IsLocalMicrophoneMuted { get; }
    IReadOnlyList<VoiceParticipantState> Participants { get; }

    event EventHandler? ParticipantsChanged;
    event EventHandler<VoiceConnectionStateChangedEventArgs>? ConnectionStateChanged;

    Task ConnectAsync(
        VoiceConnectionInfo connectionInfo,
        CancellationToken cancellationToken = default);

    // 본인의 마이크 송출만 켜거나 끕니다.
    Task SetLocalMicrophoneMutedAsync(
        bool muted,
        CancellationToken cancellationToken = default);

    // 상대 음성을 이 기기에서만 들리지 않게 합니다. 다른 팀원에게는 영향이 없습니다.
    Task SetRemotePlaybackMutedAsync(
        string participantIdentity,
        bool muted,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
