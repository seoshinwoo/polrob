using System.Collections.ObjectModel;
using polrob.Client.Voice;
using polrob.Shared;

namespace polrob.Client;

public partial class GamePlay
{
    private readonly SemaphoreSlim _voiceToggleLock = new(1, 1);
    private readonly VoiceWebViewPlatformConfiguration _voiceWebViewPlatformConfiguration = new();
    private VoiceChatService? _voiceChatService;
    private CancellationTokenSource? _voiceLifetimeCancellation;
    private bool _isTeamVoicePageActive;
    private int _voiceRosterRefreshScheduled;

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
        _voiceLifetimeCancellation?.Cancel();
        _voiceLifetimeCancellation?.Dispose();
        _voiceLifetimeCancellation = new CancellationTokenSource();
        _isTeamVoicePageActive = true;
    }

    private async Task InitializeTeamVoiceAsync()
    {
        var service = _voiceChatService;
        var cancellation = _voiceLifetimeCancellation;
        if (service == null || cancellation == null || string.IsNullOrWhiteSpace(_roomId))
        {
            SetVoiceStatus("방 정보가 없어 보이스를 시작하지 못했습니다.");
            return;
        }

        try
        {
            SetVoiceStatus("팀 보이스 연결 중...");
            await service.JoinTeamVoiceAsync(_roomId, cancellation.Token);
            if (!_isTeamVoicePageActive || cancellation.IsCancellationRequested)
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
            // 화면을 떠나며 취소된 정상적인 종료입니다.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Voice connection error: {exception}");
            SetVoiceStatus($"보이스 연결 실패 · {GetVoiceErrorMessage(exception)}");
        }
    }

    private async Task StopTeamVoiceAsync()
    {
        if (!_isTeamVoicePageActive && !(_voiceChatService?.IsConnected ?? false))
        {
            return;
        }

        _isTeamVoicePageActive = false;
        _voiceLifetimeCancellation?.Cancel();

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
            SetVoiceStatus("팀 보이스 연결 종료");
            ScheduleTeamVoiceRosterRefresh();
        }
    }

    private void OnVoiceParticipantsChanged(object? sender, EventArgs eventArgs)
    {
        if (_isTeamVoicePageActive)
        {
            ScheduleTeamVoiceRosterRefresh();
        }
    }

    private void OnVoiceConnectionStateChanged(
        object? sender,
        VoiceConnectionStateChangedEventArgs eventArgs)
    {
        if (!_isTeamVoicePageActive && eventArgs.State != VoiceConnectionState.Disconnected)
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
            VoiceConnectionState.Error => string.IsNullOrWhiteSpace(eventArgs.Message)
                ? "팀 보이스 오류"
                : $"보이스 오류 · {eventArgs.Message}",
            _ => "팀 보이스 연결 종료"
        };
        SetVoiceStatus(status);
        ScheduleTeamVoiceRosterRefresh();
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
        MainThread.BeginInvokeOnMainThread(() => VoiceStatusLabel.Text = status);
    }

    private static string GetVoiceErrorMessage(Exception exception)
    {
        return exception is VoiceChatException
            ? exception.Message
            : "잠시 후 다시 시도하세요.";
    }
}
