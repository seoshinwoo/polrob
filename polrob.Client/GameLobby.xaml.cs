using Microsoft.AspNetCore.SignalR.Client;
using polrob.Shared;

namespace polrob.Client;

[QueryProperty(nameof(RoomId), "roomId")]
[QueryProperty(nameof(RoomCode), "roomCode")]
[QueryProperty(nameof(Role), "role")]
[QueryProperty(nameof(IsHost), "isHost")]
public partial class GameLobby : ContentPage
{
    private static readonly Color PoliceSelectedColor = Color.FromArgb("#223B7CFF");
    private static readonly Color PolicePressedColor = Color.FromArgb("#363B7CFF");
    private static readonly Color RobberSelectedColor = Color.FromArgb("#22FF4F4F");
    private static readonly Color RobberPressedColor = Color.FromArgb("#36FF4F4F");

    private HubConnection? _hubConnection;
    private string _roomId = string.Empty;
    private string _roomCode = string.Empty;
    private PlayerRole _role = PlayerRole.Robber;
    private bool _isHost;
    private bool _canStartGame;
    private bool _isNavigatingToGame;

    public string RoomId
    {
        set => _roomId = value ?? string.Empty;
    }

    public string RoomCode
    {
        set
        {
            _roomCode = value ?? string.Empty;
            if (RoomCodeLabel != null)
            {
                RoomCodeLabel.Text = string.IsNullOrWhiteSpace(_roomCode)
                    ? string.Empty
                    : $"Code: {_roomCode}";
            }
        }
    }

    public string Role
    {
        set
        {
            if (Enum.TryParse<PlayerRole>(value, true, out var role))
            {
                _role = role;
            }
        }
    }

    public string IsHost
    {
        set => _isHost = bool.TryParse(value, out var isHost) && isHost;
    }

    public GameLobby()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AuthSession.LoadAsync();
        UpdateAuthHeader();

        if (string.IsNullOrWhiteSpace(_roomId))
        {
            LobbyStatusLabel.Text = "방 정보를 찾을 수 없습니다.";
            StartGameButton.IsVisible = false;
            return;
        }

        RoomCodeLabel.Text = string.IsNullOrWhiteSpace(_roomCode)
            ? string.Empty
            : $"Code: {_roomCode}";
        UpdateStartButtonVisibility();
        UpdateRoleAreaBackgrounds();

        if (!string.IsNullOrWhiteSpace(AuthSession.PlayerId))
        {
            await StartRoomUpdatesAsync(_roomId, AuthSession.PlayerId);
        }
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await DisconnectRoomUpdatesAsync(removePlayer: true);
        await Shell.Current.GoToAsync("..", true);
    }

    private async void OnProfileClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private async void OnPoliceAreaTapped(object? sender, EventArgs e)
    {
        await ChangeRoleAsync(PlayerRole.Police);
    }

    private async void OnRobberAreaTapped(object? sender, EventArgs e)
    {
        await ChangeRoleAsync(PlayerRole.Robber);
    }

    private async void OnGameStartClicked(object? sender, EventArgs e)
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            LobbyStatusLabel.Text = "방 연결을 확인할 수 없습니다.";
            return;
        }

        await _hubConnection.InvokeAsync("StartGame", _roomId);
    }

    private void UpdateAuthHeader()
    {
        ProfileButton.IsVisible = AuthSession.IsLoggedIn;
        ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
    }

    private async Task StartRoomUpdatesAsync(string roomId, string userId)
    {
        if (_hubConnection != null)
        {
            return;
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(AuthSession.ApiBaseUrl), "hubs/game-room"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<ServerResponse>("RoomStatusUpdated", response =>
        {
            MainThread.BeginInvokeOnMainThread(() => ApplyRoomStatus(response));
        });

        _hubConnection.On<ServerResponse>("GameStarted", response =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (response.Success && response.Matched)
                {
                    _ = NavigateToGameAsync(response);
                }
                else
                {
                    ApplyRoomStatus(response);
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

    private void ApplyRoomStatus(ServerResponse response)
    {
        if (!response.Success)
        {
            LobbyStatusLabel.Text = response.Message ?? "방 상태를 확인할 수 없습니다.";
            UpdateRoleAreaBackgrounds();
            return;
        }

        if (!string.IsNullOrWhiteSpace(response.RoomCode))
        {
            _roomCode = response.RoomCode;
            RoomCodeLabel.Text = $"Code: {_roomCode}";
        }

        LobbyStatusLabel.Text = $"{response.CurrentCount}/{response.MaxCount}";
        _canStartGame = response.Players.Any(p => p.Role == PlayerRole.Police)
            && response.Players.Any(p => p.Role == PlayerRole.Robber);

        if (!string.IsNullOrWhiteSpace(AuthSession.PlayerId))
        {
            var localPlayer = response.Players.FirstOrDefault(p => p.Id == AuthSession.PlayerId);
            if (localPlayer != null)
            {
                _role = localPlayer.Role;
            }
        }

        UpdateRoleAreaBackgrounds();
        UpdateStartButtonVisibility();
        RenderPlayerList(PoliceList, response.Players.Where(p => p.Role == PlayerRole.Police));
        RenderPlayerList(RobberList, response.Players.Where(p => p.Role == PlayerRole.Robber));
    }

    private async Task ChangeRoleAsync(PlayerRole role)
    {
        var targetArea = role == PlayerRole.Police ? PoliceArea : RobberArea;
        targetArea.BackgroundColor = role == PlayerRole.Police ? PolicePressedColor : RobberPressedColor;
        await Task.Delay(120);

        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            LobbyStatusLabel.Text = "방 연결을 확인할 수 없습니다.";
            UpdateRoleAreaBackgrounds();
            return;
        }

        if (string.IsNullOrWhiteSpace(AuthSession.PlayerId))
        {
            LobbyStatusLabel.Text = "로그인 정보를 확인할 수 없습니다.";
            UpdateRoleAreaBackgrounds();
            return;
        }

        await _hubConnection.InvokeAsync("ChangeRole", _roomId, AuthSession.PlayerId, role);
    }

    private void UpdateRoleAreaBackgrounds()
    {
        PoliceArea.BackgroundColor = _role == PlayerRole.Police
            ? PoliceSelectedColor
            : Colors.Transparent;

        RobberArea.BackgroundColor = _role == PlayerRole.Robber
            ? RobberSelectedColor
            : Colors.Transparent;
    }

    private void UpdateStartButtonVisibility()
    {
        StartGameButton.IsVisible = _isHost && _canStartGame;
    }

    private static void RenderPlayerList(Layout list, IEnumerable<Player> players)
    {
        list.Children.Clear();
        foreach (var player in players)
        {
            list.Children.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(player.Name) ? player.Id : player.Name,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center
            });
        }
    }

    private async Task NavigateToGameAsync(ServerResponse response)
    {
        if (_isNavigatingToGame || !response.Success || !response.Matched)
        {
            return;
        }

        _isNavigatingToGame = true;
        await DisconnectRoomUpdatesAsync(removePlayer: false);

        var roomId = Uri.EscapeDataString(_roomId);
        await Shell.Current.GoToAsync($"GamePlay?roomId={roomId}&role={_role}");
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
                if (removePlayer && !string.IsNullOrWhiteSpace(AuthSession.PlayerId))
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
            // Navigation should not be blocked by room cleanup transport errors.
        }
    }
}
