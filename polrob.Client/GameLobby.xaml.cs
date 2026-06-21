using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Maui.Controls.Shapes;
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
    private bool _isNavigatingHome;

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
                    : $"방 코드  {_roomCode}";
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
            : $"방 코드  {_roomCode}";
        UpdateStartButtonVisibility();
        UpdateRoleAreaBackgrounds();

        if (!string.IsNullOrWhiteSpace(AuthSession.UserId))
        {
            await StartRoomUpdatesAsync(_roomId, AuthSession.UserId);
        }
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        if (_isNavigatingHome)
        {
            return;
        }

        _isNavigatingHome = true;
        var leaveAcknowledged = await LeaveRoomForHomeAsync();
        if (!leaveAcknowledged)
        {
            _isNavigatingHome = false;
            return;
        }

        await Shell.Current.GoToAsync("..", false);
    }

    private async Task<bool> LeaveRoomForHomeAsync()
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            LobbyStatusLabel.Text = "서버 연결이 복구된 뒤 다시 시도해주세요.";
            return false;
        }

        var connection = _hubConnection;

        try
        {
            if (string.IsNullOrWhiteSpace(_roomId) || string.IsNullOrWhiteSpace(AuthSession.UserId))
            {
                LobbyStatusLabel.Text = "방 또는 로그인 정보를 확인할 수 없습니다.";
                return false;
            }

            LobbyStatusLabel.Text = "방에서 나가는 중...";
            var response = await connection.InvokeAsync<ServerResponse?>(
                "CancelMatchingWithAcknowledgement",
                _roomId,
                AuthSession.UserId);

            if (response == null)
            {
                LobbyStatusLabel.Text = "서버에서 방 나가기 확인 응답을 받지 못했습니다.";
                return false;
            }

            if (!response.Success)
            {
                LobbyStatusLabel.Text = response.Message ?? "서버가 방 나가기를 처리하지 못했습니다.";
                return false;
            }

            // 서버가 플레이어 제거를 완료했다는 명시적인 응답을 받은 뒤에만 이동합니다.
            _hubConnection = null;
            _ = DisposeConnectionAsync(connection);
            return true;
        }
        catch (Exception ex)
        {
            LobbyStatusLabel.Text = $"방 나가기를 확인하지 못했습니다: {ex.Message}";
            return false;
        }
    }

    private static async Task DisposeConnectionAsync(HubConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch
        {
            // 연결은 이미 로비에서 분리됐으므로 폐기 오류가 화면 이동을 막지 않습니다.
        }
    }

    private async void OnProfileClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private async void OnCopyRoomCodeClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_roomCode))
        {
            LobbyStatusLabel.Text = "복사할 방 코드가 없습니다.";
            return;
        }

        await Clipboard.Default.SetTextAsync(_roomCode);
        const string copiedMessage = "방 코드가 복사되었습니다.";
        LobbyStatusLabel.Text = copiedMessage;

        await Task.Delay(1500);
        if (LobbyStatusLabel.Text == copiedMessage)
        {
            LobbyStatusLabel.Text = string.Empty;
        }
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
        ProfileNameLabel.Text = AuthSession.Name ?? string.Empty;
    }

    private async Task StartRoomUpdatesAsync(string roomId, string userId)
    {
        if (_hubConnection != null)
        {
            return;
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(new Uri(AuthSession.ApiBaseUrl), "hubs/game-room"),
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                })
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
                && !string.IsNullOrWhiteSpace(AuthSession.UserId))
            {
                await _hubConnection.InvokeAsync("JoinRoom", _roomId, AuthSession.UserId);
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
            RoomCodeLabel.Text = $"방 코드  {_roomCode}";
        }

        LobbyStatusLabel.Text = string.Empty;
        _canStartGame = response.Players.Any(p => p.Role == PlayerRole.Police)
            && response.Players.Any(p => p.Role == PlayerRole.Robber);

        if (!string.IsNullOrWhiteSpace(AuthSession.UserId))
        {
            var localPlayer = response.Players.FirstOrDefault(p => p.Id == AuthSession.UserId);
            if (localPlayer != null)
            {
                _role = localPlayer.Role;
            }
        }

        UpdateRoleAreaBackgrounds();
        UpdateStartButtonVisibility();
        RenderPlayerList(PoliceList, response.Players.Where(p => p.Role == PlayerRole.Police), PlayerRole.Police);
        RenderPlayerList(RobberList, response.Players.Where(p => p.Role == PlayerRole.Robber), PlayerRole.Robber);
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

        if (string.IsNullOrWhiteSpace(AuthSession.UserId))
        {
            LobbyStatusLabel.Text = "로그인 정보를 확인할 수 없습니다.";
            UpdateRoleAreaBackgrounds();
            return;
        }

        await _hubConnection.InvokeAsync("ChangeRole", _roomId, AuthSession.UserId, role);
    }

    private void UpdateRoleAreaBackgrounds()
    {
        PoliceArea.BackgroundColor = _role == PlayerRole.Police
            ? PoliceSelectedColor
            : Colors.Transparent;
        PoliceArea.Stroke = _role == PlayerRole.Police
            ? Color.FromArgb("#FFF06A")
            : Color.FromArgb("#F7D9A0");

        RobberArea.BackgroundColor = _role == PlayerRole.Robber
            ? RobberSelectedColor
            : Colors.Transparent;
        RobberArea.Stroke = _role == PlayerRole.Robber
            ? Color.FromArgb("#FFF06A")
            : Color.FromArgb("#F7D9A0");
    }

    private void UpdateStartButtonVisibility()
    {
        StartGameButton.IsVisible = _isHost && _canStartGame;
    }

    private void RenderPlayerList(Layout list, IEnumerable<Player> players, PlayerRole role)
    {
        list.Children.Clear();
        foreach (var player in players)
        {
            var isLocalPlayer = player.Id == AuthSession.UserId;
            var card = new Border
            {
                HeightRequest = 64,
                Padding = new Thickness(7, 5),
                BackgroundColor = role == PlayerRole.Police
                    ? Color.FromArgb("#15366D")
                    : Color.FromArgb("#672010"),
                Stroke = isLocalPlayer
                    ? Color.FromArgb("#FFD84B")
                    : role == PlayerRole.Police
                        ? Color.FromArgb("#2369BD")
                        : Color.FromArgb("#B83A18"),
                StrokeThickness = isLocalPlayer ? 3 : 2,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) }
            };

            var content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(48)),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 6
            };

            var avatarFrame = new Border
            {
                WidthRequest = 46,
                HeightRequest = 46,
                Padding = 0,
                BackgroundColor = role == PlayerRole.Police
                    ? Color.FromArgb("#0D5DB7")
                    : Color.FromArgb("#A93214"),
                Stroke = role == PlayerRole.Police
                    ? Color.FromArgb("#59C8FF")
                    : Color.FromArgb("#FF8A3D"),
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(23) },
                Content = new Image
                {
                    Source = ImageSource.FromFile(role == PlayerRole.Police
                        ? "lobby_police_avatar.png"
                        : "lobby_robber_avatar.png"),
                    Aspect = Aspect.AspectFit
                }
            };

            var nameLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(player.Name) ? player.Id : player.Name,
                FontFamily = "OpenSansSemibold",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Center
            };

            content.Add(avatarFrame);
            content.Add(nameLabel, 1);
            card.Content = content;
            list.Children.Add(card);
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
        var roomCode = Uri.EscapeDataString(_roomCode);
        var role = Uri.EscapeDataString(_role.ToString());
        var isHost = _isHost.ToString().ToLowerInvariant();

        await Shell.Current.GoToAsync($"GamePlay?roomId={roomId}&role={role}&gameType=custom&roomCode={roomCode}&isHost={isHost}");
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
                if (removePlayer && !string.IsNullOrWhiteSpace(AuthSession.UserId))
                {
                    await connection.InvokeAsync("CancelMatching", _roomId, AuthSession.UserId);
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
