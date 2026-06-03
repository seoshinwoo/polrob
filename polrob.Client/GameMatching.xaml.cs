using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using polrob.Shared;

namespace polrob.Client;

[QueryProperty(nameof(Role), "role")]
public partial class GameMatching : ContentPage
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri(AuthSession.ApiBaseUrl)
    };

    private bool _hasRequestedMatching;
    private PlayerRole _selectedRole = PlayerRole.Robber;
    private HubConnection? _hubConnection;
    private string? _roomId;
    private bool _isMatched;

    public string Role
    {
        set
        {
            if (Enum.TryParse<PlayerRole>(value, true, out var role))
            {
                _selectedRole = role;
            }
        }
    }

    public GameMatching()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasRequestedMatching)
        {
            return;
        }

        await AuthSession.LoadAsync();

        if (!AuthSession.IsLoggedIn || string.IsNullOrWhiteSpace(AuthSession.PlayerId))
        {
            await Shell.Current.GoToAsync("Login");
            return;
        }

        await JoinRandomGameAsync(AuthSession.PlayerId, _selectedRole);
    }

    private async void OnCancelMatchingClicked(object sender, EventArgs e)
    {
        MatchingStatusLabel.Text = "매칭 취소 중...";
        MatchingActivityIndicator.IsRunning = false;

        await DisconnectRoomUpdatesAsync(removePlayer: true);
        _hasRequestedMatching = false;

        await Shell.Current.GoToAsync("..");
    }

    public void UpdateMatchingCount(int currentCount, int maxCount = 6)
    {
        MatchingStatusLabel.Text = $"매칭 중({currentCount}/{maxCount})";
    }

    private async Task JoinRandomGameAsync(string userId, PlayerRole role)
    {
        try
        {
            _hasRequestedMatching = true;

            var response = await _httpClient.PostAsJsonAsync(
                "game/join-random",
                new JoinRandomGameRequest(userId, role));

            if (!response.IsSuccessStatusCode)
            {
                MatchingStatusLabel.Text = await ReadErrorMessageAsync(response);
                MatchingActivityIndicator.IsRunning = false;
                _hasRequestedMatching = false;
                return;
            }

            var serverResponse = await response.Content.ReadFromJsonAsync<ServerResponse>();
            if (serverResponse?.Success == false && !string.IsNullOrWhiteSpace(serverResponse.Message))
            {
                MatchingStatusLabel.Text = serverResponse.Message;
                MatchingActivityIndicator.IsRunning = false;
                _hasRequestedMatching = false;
            }

            if (serverResponse?.Success == true)
            {
                _roomId = serverResponse.RoomId;
                _isMatched = serverResponse.Matched;
                UpdateMatchingCount(serverResponse.CurrentCount, serverResponse.MaxCount);

                if (!string.IsNullOrWhiteSpace(_roomId)
                    && !string.IsNullOrWhiteSpace(AuthSession.PlayerId))
                {
                    await StartRoomUpdatesAsync(_roomId, AuthSession.PlayerId);
                }
            }
        }
        catch (HttpRequestException)
        {
            MatchingStatusLabel.Text = "서버에 연결할 수 없습니다.";
            MatchingActivityIndicator.IsRunning = false;
            _hasRequestedMatching = false;
        }
        catch (Exception ex)
        {
            MatchingStatusLabel.Text = $"매칭 요청 중 오류가 발생했습니다: {ex.Message}";
            MatchingActivityIndicator.IsRunning = false;
            _hasRequestedMatching = false;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _ = DisconnectRoomUpdatesAsync(removePlayer: !_isMatched);
    }

    private async Task StartRoomUpdatesAsync(string roomId, string userId)
    {
        await DisconnectRoomUpdatesAsync(removePlayer: false);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(AuthSession.ApiBaseUrl), "hubs/game-room"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<ServerResponse>("RoomStatusUpdated", response =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (response.Success)
                {
                    UpdateMatchingCount(response.CurrentCount, response.MaxCount);
                    _isMatched = response.Matched;

                    if (response.Matched)
                    {
                        MatchingStatusLabel.Text = $"매칭 완료({response.CurrentCount}/{response.MaxCount})";
                        MatchingActivityIndicator.IsRunning = false;
                    }

                    return;
                }

                if (!string.IsNullOrWhiteSpace(response.Message))
                {
                    MatchingStatusLabel.Text = response.Message;
                    MatchingActivityIndicator.IsRunning = false;
                }
            });
        });

        _hubConnection.Reconnected += async _ =>
        {
            if (!string.IsNullOrWhiteSpace(_roomId)
                && !string.IsNullOrWhiteSpace(AuthSession.PlayerId))
            {
                await _hubConnection.InvokeAsync("JoinRoom", _roomId, AuthSession.PlayerId);
            }
        };

        await _hubConnection.StartAsync();
        await _hubConnection.InvokeAsync("JoinRoom", roomId, userId);
    }

    private async Task DisconnectRoomUpdatesAsync(bool removePlayer)
    {
        if (_hubConnection == null)
        {
            return;
        }

        var connection = _hubConnection;
        _hubConnection = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(_roomId)
                && connection.State == HubConnectionState.Connected)
            {
                if (removePlayer
                    && !_isMatched
                    && !string.IsNullOrWhiteSpace(AuthSession.PlayerId))
                {
                    await connection.InvokeAsync("CancelMatching", _roomId, AuthSession.PlayerId);
                }
                else
                {
                    await connection.InvokeAsync("LeaveRoom", _roomId);
                }
            }

            await connection.DisposeAsync();
        }
        catch
        {
            // Closing the matching page should not surface transport cleanup errors.
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var message = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(message)
            ? $"매칭 요청이 실패했습니다. ({(int)response.StatusCode})"
            : message.Trim('"');
    }

    private sealed record JoinRandomGameRequest(string UserId, PlayerRole Role);
}
