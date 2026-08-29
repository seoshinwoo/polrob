using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.ApplicationModel;
using polrob.Shared;

namespace polrob.Client.Voice;

// LiveKit의 공식 JavaScript SDK를 MAUI HybridWebView 안에서 실행하는 어댑터입니다.
// API key/secret은 이 클라이언트로 전달되지 않고, 서버에서 발급한 짧은 토큰만 사용합니다.
public sealed class HybridWebViewVoiceRoomClient : IVoiceRoomClient
{
    private static readonly TimeSpan WebViewReadyTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HybridWebView _webView;
    private readonly TaskCompletionSource _webViewReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<VoiceCommandResult>>
        _pendingCommands = new();
    private readonly object _participantsLock = new();
    private IReadOnlyList<VoiceParticipantState> _participants = Array.Empty<VoiceParticipantState>();
    private bool _disposed;

    public HybridWebViewVoiceRoomClient(HybridWebView webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _webView.RawMessageReceived += OnRawMessageReceived;
    }

    public bool IsConnected { get; private set; }
    public bool IsLocalMicrophoneMuted { get; private set; } = true;

    public IReadOnlyList<VoiceParticipantState> Participants
    {
        get
        {
            lock (_participantsLock)
            {
                return _participants;
            }
        }
    }

    public event EventHandler? ParticipantsChanged;
    public event EventHandler<VoiceConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public async Task ConnectAsync(
        VoiceConnectionInfo connectionInfo,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(connectionInfo);

        if (string.IsNullOrWhiteSpace(connectionInfo.ServerUrl) ||
            string.IsNullOrWhiteSpace(connectionInfo.ParticipantToken))
        {
            throw new VoiceChatException("LiveKit 접속 주소 또는 참가자 토큰이 없습니다.");
        }

        if (IsConnected)
        {
            return;
        }

        ConnectionStateChanged?.Invoke(
            this,
            new VoiceConnectionStateChangedEventArgs(VoiceConnectionState.Connecting));

        await _webViewReady.Task.WaitAsync(WebViewReadyTimeout, cancellationToken);

        // 권한이 거부되어도 방에는 수신 전용으로 접속하므로 게임 자체는 계속할 수 있습니다.
        var microphoneGranted = await RequestMicrophonePermissionAsync(cancellationToken);
        IsLocalMicrophoneMuted = !microphoneGranted;

        await SendCommandAsync(
            "connect",
            new Dictionary<string, object?>
            {
                ["url"] = connectionInfo.ServerUrl,
                ["token"] = connectionInfo.ParticipantToken,
                ["enableMicrophone"] = microphoneGranted
            },
            cancellationToken);
    }

    public async Task SetLocalMicrophoneMutedAsync(
        bool muted,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!muted && !await RequestMicrophonePermissionAsync(cancellationToken))
        {
            throw new VoiceChatException("마이크 권한이 없어 마이크를 켤 수 없습니다.");
        }

        await SendCommandAsync(
            "setLocalMuted",
            new Dictionary<string, object?> { ["muted"] = muted },
            cancellationToken);
    }

    public Task SetRemotePlaybackMutedAsync(
        string participantIdentity,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(participantIdentity))
        {
            throw new ArgumentException("참가자 ID가 필요합니다.", nameof(participantIdentity));
        }

        return SendCommandAsync(
            "setRemoteMuted",
            new Dictionary<string, object?>
            {
                ["identity"] = participantIdentity,
                ["muted"] = muted
            },
            cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed && !IsConnected)
        {
            return;
        }

        if (_webViewReady.Task.IsCompleted)
        {
            try
            {
                await SendCommandAsync("disconnect", null, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 화면 종료 중에는 WebView가 먼저 제거될 수 있으므로 로컬 상태 정리는 계속합니다.
                System.Diagnostics.Debug.WriteLine($"Voice disconnect warning: {ex.Message}");
            }
        }

        SetDisconnectedState();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync();
        _disposed = true;
        _webView.RawMessageReceived -= OnRawMessageReceived;

        foreach (var (_, completion) in _pendingCommands)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(HybridWebViewVoiceRoomClient)));
        }
        _pendingCommands.Clear();
    }

    private async Task SendCommandAsync(
        string commandType,
        IReadOnlyDictionary<string, object?>? values,
        CancellationToken cancellationToken)
    {
        await _webViewReady.Task.WaitAsync(WebViewReadyTimeout, cancellationToken);

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<VoiceCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingCommands.TryAdd(requestId, completion))
        {
            throw new VoiceChatException("보이스 명령을 만들지 못했습니다.");
        }

        try
        {
            var command = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = commandType,
                ["requestId"] = requestId
            };

            if (values != null)
            {
                foreach (var (key, value) in values)
                {
                    command[key] = value;
                }
            }

            var json = JsonSerializer.Serialize(command, JsonOptions);
            await MainThread.InvokeOnMainThreadAsync(() => _webView.SendRawMessage(json));

            var result = await completion.Task.WaitAsync(CommandTimeout, cancellationToken);
            if (!result.Success)
            {
                throw new VoiceChatException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? "보이스 명령 처리에 실패했습니다."
                        : result.Error);
            }
        }
        catch (TimeoutException ex)
        {
            throw new VoiceChatException("LiveKit 응답 시간이 초과되었습니다.", ex);
        }
        finally
        {
            _pendingCommands.TryRemove(requestId, out _);
        }
    }

    private static async Task<bool> RequestMicrophonePermissionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted && Permissions.ShouldShowRationale<Permissions.Microphone>())
        {
            System.Diagnostics.Debug.WriteLine("Voice chat requires microphone permission.");
        }

        if (status != PermissionStatus.Granted)
        {
            status = await MainThread.InvokeOnMainThreadAsync(
                Permissions.RequestAsync<Permissions.Microphone>);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return status == PermissionStatus.Granted;
    }

    private void OnRawMessageReceived(
        object? sender,
        HybridWebViewRawMessageReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Message))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(eventArgs.Message);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "ready":
                    _webViewReady.TrySetResult();
                    break;
                case "commandResult":
                    HandleCommandResult(root);
                    break;
                case "participants":
                    HandleParticipants(root);
                    break;
                case "connection":
                    HandleConnectionState(root);
                    break;
                case "warning":
                case "error":
                    var message = TryGetString(root, "message");
                    ConnectionStateChanged?.Invoke(
                        this,
                        new VoiceConnectionStateChangedEventArgs(
                            VoiceConnectionState.Error,
                            message));
                    break;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Invalid voice bridge message: {ex.Message}");
        }
    }

    private void HandleCommandResult(JsonElement root)
    {
        var requestId = TryGetString(root, "requestId");
        if (string.IsNullOrWhiteSpace(requestId) ||
            !_pendingCommands.TryGetValue(requestId, out var completion))
        {
            return;
        }

        var success = root.TryGetProperty("success", out var successElement) &&
                      successElement.ValueKind == JsonValueKind.True;
        completion.TrySetResult(new VoiceCommandResult(
            success,
            TryGetString(root, "error")));
    }

    private void HandleParticipants(JsonElement root)
    {
        if (!root.TryGetProperty("participants", out var participantsElement))
        {
            return;
        }

        var participantDtos = participantsElement.Deserialize<List<VoiceParticipantDto>>(JsonOptions)
                              ?? new List<VoiceParticipantDto>();
        var participants = participantDtos
            .Where(participant => !string.IsNullOrWhiteSpace(participant.Identity))
            .Select(participant => new VoiceParticipantState(
                participant.Identity!,
                string.IsNullOrWhiteSpace(participant.Name)
                    ? participant.Identity!
                    : participant.Name!,
                participant.IsLocal,
                participant.IsSpeaking,
                participant.HasMicrophoneTrack,
                participant.IsMuted))
            .ToArray();

        lock (_participantsLock)
        {
            _participants = participants;
            IsLocalMicrophoneMuted = participants
                .FirstOrDefault(participant => participant.IsLocal)?.IsMuted
                ?? IsLocalMicrophoneMuted;
        }

        ParticipantsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleConnectionState(JsonElement root)
    {
        var rawState = TryGetString(root, "state");
        var state = rawState switch
        {
            "connecting" => VoiceConnectionState.Connecting,
            "connected" => VoiceConnectionState.Connected,
            "reconnecting" => VoiceConnectionState.Reconnecting,
            "error" => VoiceConnectionState.Error,
            _ => VoiceConnectionState.Disconnected
        };

        IsConnected = state is VoiceConnectionState.Connected or VoiceConnectionState.Reconnecting;
        if (state == VoiceConnectionState.Disconnected)
        {
            SetDisconnectedState(raiseConnectionEvent: false);
        }

        ConnectionStateChanged?.Invoke(
            this,
            new VoiceConnectionStateChangedEventArgs(
                state,
                TryGetString(root, "message")));
    }

    private void SetDisconnectedState(bool raiseConnectionEvent = true)
    {
        IsConnected = false;
        IsLocalMicrophoneMuted = true;
        lock (_participantsLock)
        {
            _participants = Array.Empty<VoiceParticipantState>();
        }

        ParticipantsChanged?.Invoke(this, EventArgs.Empty);
        if (raiseConnectionEvent)
        {
            ConnectionStateChanged?.Invoke(
                this,
                new VoiceConnectionStateChangedEventArgs(VoiceConnectionState.Disconnected));
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record VoiceCommandResult(bool Success, string? Error);

    private sealed class VoiceParticipantDto
    {
        [JsonPropertyName("identity")]
        public string? Identity { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("isLocal")]
        public bool IsLocal { get; init; }

        [JsonPropertyName("isSpeaking")]
        public bool IsSpeaking { get; init; }

        [JsonPropertyName("hasMicrophoneTrack")]
        public bool HasMicrophoneTrack { get; init; }

        [JsonPropertyName("isMuted")]
        public bool IsMuted { get; init; }
    }
}
