namespace polrob.Client.Voice;

// LiveKit 참가자 상태를 UI가 이해할 수 있는 작은 형태로 변환한 값입니다.
public sealed record VoiceParticipantState(
    string Identity,
    string Name,
    bool IsLocal,
    bool IsSpeaking,
    bool HasMicrophoneTrack,
    bool IsMuted);

public sealed class VoiceConnectionStateChangedEventArgs : EventArgs
{
    public VoiceConnectionStateChangedEventArgs(
        VoiceConnectionState state,
        string? message = null)
    {
        State = state;
        Message = message;
    }

    public VoiceConnectionState State { get; }
    public string? Message { get; }
}

public enum VoiceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}
