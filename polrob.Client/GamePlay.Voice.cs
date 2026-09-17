using System.Collections.ObjectModel;
using polrob.Client.Voice;
using polrob.Shared;

namespace polrob.Client;

public partial class GamePlay
{
    private static readonly Color[] TeamVoiceSlotColors =
    [
        Color.FromArgb("#D8B62E"),
        Color.FromArgb("#E57C2F"),
        Color.FromArgb("#3977D7"),
        Color.FromArgb("#20B69A"),
        Color.FromArgb("#A768D4"),
        Color.FromArgb("#D94D68")
    ];

    private readonly SemaphoreSlim _voiceToggleLock = new(1, 1);
    private readonly SemaphoreSlim _voiceConnectionLock = new(1, 1);
    private readonly object _voiceLifetimeGate = new();
    private readonly VoiceWebViewPlatformConfiguration _voiceWebViewPlatformConfiguration = new();
    private VoiceChatService? _voiceChatService;
    private CancellationTokenSource? _voiceLifetimeCancellation;
    private Window? _voiceLifecycleWindow;
    private bool _isTeamVoicePageActive;
    private bool _resumeTeamVoiceAfterWindowStop;
    private int _voiceRosterRefreshScheduled;
    private int _voiceReconnectGeneration;
    private int _voiceReconnectWorkerRunning;
    private Task _voiceStopTask = Task.CompletedTask;

    public ObservableCollection<TeamVoiceMemberViewModel> VoiceMembers { get; } = new();

    private void InitializeTeamVoiceControls()
    {
        _voiceWebViewPlatformConfiguration.Attach(VoiceWebView);

        var roomClient = new HybridWebViewVoiceRoomClient(VoiceWebView);
        _voiceChatService = new VoiceChatService(new VoiceTokenClient(), roomClient);
        _voiceChatService.ParticipantsChanged += OnVoiceParticipantsChanged;
        _voiceChatService.ConnectionStateChanged += OnVoiceConnectionStateChanged;

        RefreshTeamVoiceRoster();
    }

    private void BeginTeamVoiceLifetime()
    {
        lock (_voiceLifetimeGate)
        {
            if (_isTeamVoicePageActive && _voiceLifetimeCancellation != null)
            {
                return;
            }

            _voiceLifetimeCancellation = new CancellationTokenSource();
            _isTeamVoicePageActive = true;
        }
    }

    private async Task InitializeTeamVoiceAsync()
    {
        await _voiceConnectionLock.WaitAsync();
        try
        {
            var service = _voiceChatService;
            CancellationTokenSource? cancellation;
            lock (_voiceLifetimeGate)
            {
                cancellation = _isTeamVoicePageActive
                    ? _voiceLifetimeCancellation
                    : null;
            }
            if (service == null || cancellation == null || string.IsNullOrWhiteSpace(_roomId))
            {
                SetVoiceStatus("방 정보가 없어 보이스를 시작하지 못했습니다.");
                return;
            }

            try
            {
                SetVoiceStatus("팀 보이스 연결 중...");
                await service.JoinTeamVoiceAsync(_roomId, cancellation.Token);
                if (!IsCurrentTeamVoiceLifetime(cancellation))
                {
                    return;
                }

                SetVoiceStatus(service.IsLocalMicrophoneMuted
                    ? "연결됨 · 내 마이크 꺼짐"
                    : "연결됨 · 내 마이크 켜짐");
                ScheduleTeamVoiceRosterRefresh();
            }
            catch (OperationCanceledException)
            {
                // 화면 이탈 또는 앱 백그라운드 전환으로 취소된 정상적인 종료입니다.
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Voice connection error: {exception}");
                if (IsCurrentTeamVoiceLifetime(cancellation))
                {
                    SetVoiceStatus($"보이스 연결 실패 · {GetVoiceErrorMessage(exception)}");
                }
            }
        }
        finally
        {
            _voiceConnectionLock.Release();
        }
    }

    private Task StopTeamVoiceAsync()
    {
        lock (_voiceLifetimeGate)
        {
            if (!_isTeamVoicePageActive)
            {
                return _voiceStopTask;
            }

            _isTeamVoicePageActive = false;
            var lifetimeToStop = _voiceLifetimeCancellation;
            _voiceLifetimeCancellation = null;
            lifetimeToStop?.Cancel();
            _voiceStopTask = StopTeamVoiceCoreAsync(lifetimeToStop);
            return _voiceStopTask;
        }
    }

    private async Task StopTeamVoiceCoreAsync(CancellationTokenSource? lifetimeToStop)
    {
        await _voiceConnectionLock.WaitAsync();
        try
        {
            if (_voiceChatService != null)
            {
                await _voiceChatService.LeaveAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Voice cleanup error: {exception}");
        }
        finally
        {
            lifetimeToStop?.Dispose();
            if (!IsTeamVoiceLifetimeActive())
            {
                SetVoiceStatus("팀 보이스 연결 종료");
                ScheduleTeamVoiceRosterRefresh();
            }
            _voiceConnectionLock.Release();
        }
    }

    private bool IsCurrentTeamVoiceLifetime(CancellationTokenSource lifetime)
    {
        lock (_voiceLifetimeGate)
        {
            return _isTeamVoicePageActive &&
                   ReferenceEquals(_voiceLifetimeCancellation, lifetime) &&
                   !lifetime.IsCancellationRequested;
        }
    }

    private bool IsTeamVoiceLifetimeActive()
    {
        lock (_voiceLifetimeGate)
        {
            return _isTeamVoicePageActive;
        }
    }

    private void AttachTeamVoiceWindowLifecycle()
    {
        var window = Window;
        if (ReferenceEquals(_voiceLifecycleWindow, window))
        {
            return;
        }

        DetachTeamVoiceWindowLifecycle();
        if (window == null)
        {
            return;
        }

        _voiceLifecycleWindow = window;
        window.Stopped += OnVoiceWindowStopped;
        window.Resumed += OnVoiceWindowResumed;
    }

    private void DetachTeamVoiceWindowLifecycle()
    {
        if (_voiceLifecycleWindow == null)
        {
            return;
        }

        _voiceLifecycleWindow.Stopped -= OnVoiceWindowStopped;
        _voiceLifecycleWindow.Resumed -= OnVoiceWindowResumed;
        _voiceLifecycleWindow = null;
    }

    private async void OnVoiceWindowStopped(object? sender, EventArgs eventArgs)
    {
        _resumeTeamVoiceAfterWindowStop = IsTeamVoiceLifetimeActive();
        if (_resumeTeamVoiceAfterWindowStop)
        {
            await StopTeamVoiceAsync();
        }
    }

    private async void OnVoiceWindowResumed(object? sender, EventArgs eventArgs)
    {
        if (!_resumeTeamVoiceAfterWindowStop || IsTeamVoiceLifetimeActive())
        {
            return;
        }

        _resumeTeamVoiceAfterWindowStop = false;
        BeginTeamVoiceLifetime();
        if (!await RefreshOrReconnectGameNetworkAsync())
        {
            await StopTeamVoiceAsync();
            SetVoiceStatus("게임 서버 재연결 실패 · 팀 보이스를 시작할 수 없습니다.");
            return;
        }

        await InitializeTeamVoiceAsync();
    }

    private void OnVoiceParticipantsChanged(object? sender, EventArgs eventArgs)
    {
        if (IsTeamVoiceLifetimeActive())
        {
            ScheduleTeamVoiceRosterRefresh();
        }
    }

    private void OnVoiceConnectionStateChanged(
        object? sender,
        VoiceConnectionStateChangedEventArgs eventArgs)
    {
        if (!IsTeamVoiceLifetimeActive() && eventArgs.State != VoiceConnectionState.Disconnected)
        {
            return;
        }

        var status = eventArgs.State switch
        {
            VoiceConnectionState.Connecting => "팀 보이스 연결 중...",
            VoiceConnectionState.Connected => _voiceChatService?.IsLocalMicrophoneMuted == true
                ? "연결됨 · 내 마이크 꺼짐"
                : "연결됨 · 내 마이크 켜짐",
            VoiceConnectionState.Reconnecting => "팀 보이스 재연결 중...",
            VoiceConnectionState.Warning => string.IsNullOrWhiteSpace(eventArgs.Message)
                ? "팀 보이스 경고"
                : $"보이스 경고 · {eventArgs.Message}",
            VoiceConnectionState.Error => string.IsNullOrWhiteSpace(eventArgs.Message)
                ? "팀 보이스 오류"
                : $"보이스 오류 · {eventArgs.Message}",
            _ => "팀 보이스 연결 종료"
        };
        SetVoiceStatus(status);
        ScheduleTeamVoiceRosterRefresh();
        if (eventArgs.State == VoiceConnectionState.Disconnected &&
            IsTeamVoiceLifetimeActive())
        {
            ScheduleTeamVoiceReconnect();
        }
    }

    private void ScheduleTeamVoiceReconnect()
    {
        Interlocked.Increment(ref _voiceReconnectGeneration);
        if (Interlocked.CompareExchange(ref _voiceReconnectWorkerRunning, 1, 0) != 0)
        {
            return;
        }

        _ = ReconnectTeamVoiceAsync();
    }

    private async Task ReconnectTeamVoiceAsync()
    {
        var handledGeneration = 0;
        try
        {
            while (IsTeamVoiceLifetimeActive())
            {
                var requestedGeneration = Volatile.Read(ref _voiceReconnectGeneration);
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (!IsTeamVoiceLifetimeActive())
                {
                    return;
                }

                if (!(_voiceChatService?.IsConnected ?? false))
                {
                    await InitializeTeamVoiceAsync();
                }

                handledGeneration = requestedGeneration;
                if (requestedGeneration == Volatile.Read(ref _voiceReconnectGeneration))
                {
                    return;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _voiceReconnectWorkerRunning, 0);
            if (IsTeamVoiceLifetimeActive() &&
                !(_voiceChatService?.IsConnected ?? false) &&
                handledGeneration != Volatile.Read(ref _voiceReconnectGeneration) &&
                Interlocked.CompareExchange(ref _voiceReconnectWorkerRunning, 1, 0) == 0)
            {
                _ = ReconnectTeamVoiceAsync();
            }
        }
    }

    private void ScheduleTeamVoiceRosterRefresh()
    {
        if (Interlocked.Exchange(ref _voiceRosterRefreshScheduled, 1) == 1)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Interlocked.Exchange(ref _voiceRosterRefreshScheduled, 0);
            RefreshTeamVoiceRoster();
        });
    }

    private void RefreshTeamVoiceRoster()
    {
        if (!MainThread.IsMainThread)
        {
            ScheduleTeamVoiceRosterRefresh();
            return;
        }

        List<Player> gamePlayers;
        try
        {
            gamePlayers = _players.Values
                .Where(player => player.Role == _player.Role)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            // 네트워크 콜백이 플레이어 사전을 갱신 중이면 다음 UI 턴에서 다시 합칩니다.
            ScheduleTeamVoiceRosterRefresh();
            return;
        }

        if (gamePlayers.All(player => player.Id != _player.Id))
        {
            gamePlayers.Add(_player);
        }

        var voiceStates = (_voiceChatService?.Participants ?? Array.Empty<VoiceParticipantState>())
            .ToDictionary(participant => participant.Identity, StringComparer.Ordinal);
        var orderedPlayers = gamePlayers
            .GroupBy(player => player.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(player => player.Id == _player.Id)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wantedIdentities = orderedPlayers
            .Select(player => player.Id)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = VoiceMembers.Count - 1; index >= 0; index--)
        {
            if (!wantedIdentities.Contains(VoiceMembers[index].Identity))
            {
                VoiceMembers.RemoveAt(index);
            }
        }

        for (var targetIndex = 0; targetIndex < orderedPlayers.Count; targetIndex++)
        {
            var gamePlayer = orderedPlayers[targetIndex];
            var member = VoiceMembers.FirstOrDefault(item => item.Identity == gamePlayer.Id);
            if (member == null)
            {
                member = new TeamVoiceMemberViewModel(gamePlayer.Id, gamePlayer.Name);
                VoiceMembers.Insert(Math.Min(targetIndex, VoiceMembers.Count), member);
            }
            else
            {
                var currentIndex = VoiceMembers.IndexOf(member);
                if (currentIndex != targetIndex)
                {
                    VoiceMembers.Move(currentIndex, targetIndex);
                }
            }

            var isLocal = gamePlayer.Id == _player.Id;
            voiceStates.TryGetValue(gamePlayer.Id, out var voiceState);
            var muted = voiceState?.IsMuted ?? (isLocal
                ? _voiceChatService?.IsLocalMicrophoneMuted ?? true
                : member.IsMuted);

            member.Update(
                string.IsNullOrWhiteSpace(gamePlayer.Name) ? gamePlayer.Id : gamePlayer.Name,
                targetIndex + 1,
                TeamVoiceSlotColors[targetIndex % TeamVoiceSlotColors.Length],
                isLocal,
                voiceState?.IsSpeaking ?? false,
                muted,
                voiceState != null && (_voiceChatService?.IsConnected ?? false));
        }
    }

    private async void OnVoiceMemberTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (eventArgs.Parameter is not string identity || _voiceChatService == null)
        {
            return;
        }

        var member = VoiceMembers.FirstOrDefault(item => item.Identity == identity);
        if (member == null || member.IsBusy)
        {
            return;
        }

        if (!_voiceChatService.IsConnected || !member.IsVoiceConnected)
        {
            SetVoiceStatus("해당 팀원의 보이스가 아직 연결되지 않았습니다.");
            return;
        }

        await _voiceToggleLock.WaitAsync();
        member.IsBusy = true;
        try
        {
            var muted = !member.IsMuted;
            if (member.IsLocal)
            {
                await _voiceChatService.SetLocalMicrophoneMutedAsync(muted);
                SetVoiceStatus(muted
                    ? "연결됨 · 내 마이크 꺼짐"
                    : "연결됨 · 내 마이크 켜짐");
            }
            else
            {
                await _voiceChatService.SetRemotePlaybackMutedAsync(identity, muted);
            }

            member.IsMuted = muted;
            ScheduleTeamVoiceRosterRefresh();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Voice mute toggle error: {exception}");
            SetVoiceStatus($"음소거 변경 실패 · {GetVoiceErrorMessage(exception)}");
        }
        finally
        {
            member.IsBusy = false;
            _voiceToggleLock.Release();
        }
    }

    private void SetVoiceStatus(string status)
    {
        System.Diagnostics.Debug.WriteLine($"Team voice: {status}");

        void ApplyStatus()
        {
            VoiceStatusLabel.Text = status;
            VoiceStatusLabel.TextColor = status.Contains("실패", StringComparison.Ordinal) ||
                                         status.Contains("오류", StringComparison.Ordinal)
                ? Color.FromArgb("#FCA5A5")
                : status.StartsWith("연결됨", StringComparison.Ordinal)
                    ? Color.FromArgb("#86EFAC")
                    : status.Contains("경고", StringComparison.Ordinal) ||
                      status.Contains("연결 중", StringComparison.Ordinal) ||
                      status.Contains("재연결", StringComparison.Ordinal)
                        ? Color.FromArgb("#FDE68A")
                        : Color.FromArgb("#CBD5E1");
        }

        if (MainThread.IsMainThread)
        {
            ApplyStatus();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(ApplyStatus);
        }
    }

    private static string GetVoiceErrorMessage(Exception exception)
    {
        return exception is VoiceChatException
            ? exception.Message
            : "잠시 후 다시 시도하세요.";
    }
}
