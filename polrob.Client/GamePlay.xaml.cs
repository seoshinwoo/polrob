using System.Linq;
using Microsoft.Maui.Devices;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using polrob.Shared;
using polrob.Client.Network;

namespace polrob.Client;

[QueryProperty(nameof(RoomId), "roomId")]
[QueryProperty(nameof(Role), "role")]
[QueryProperty(nameof(GameType), "gameType")]
[QueryProperty(nameof(RoomCode), "roomCode")]
[QueryProperty(nameof(IsHost), "isHost")]
public partial class GamePlay : ContentPage
{
    private Player _player;
    private Dictionary<string, Player> _players = new();

    // Shared Map
    private GameMap _gameMap;

    // Joystick state
    private SKPoint _joystickCenter;
    private SKPoint _joystickThumb;
    private float _joystickRadius = 150f;
    private float _thumbRadius = 50f;
    private long _activeTouchId = -1;

    private readonly IDispatcherTimer _timer;
    private SKCanvasView _canvas;
    private GameNetworkClient? _networkClient;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private bool _lastSyncedIsMoving;
    private readonly Dictionary<string, RemotePlayerInterpolationState> _remotePlayerInterpolations = new();
    private GamePhase _gamePhase = GamePhase.Waiting;
    private int _remainingTime = 300;
    private PlayerRole? _winnerRole;
    private bool _isGameOverTransitioning = false;
    private static readonly TimeSpan MovingUdpSyncInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan StoppedUdpSyncInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RemoteInterpolationDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan RemoteSnapshotResetGap = TimeSpan.FromMilliseconds(250);
    private const int MaxRemoteMovementSnapshots = 4;
    private const float VisionRangePlayerSizeMultiplier = 2.5f;
    private const float VisionConeAngleDegrees = 90f;
    private const byte FogOpacity = 120;
    private const float PlayerNameFontSize = 28f;
    private const float PlayerNameMaxWidth = 180f;
    private const byte BushPlayerOpacity = 185;
    private const float RenderCullPadding = 100f;
    private readonly List<Obstacle> _nearbyCollisionObstacles = new();
    private readonly SemaphoreSlim _assetLoadLock = new(1, 1);
    private bool _assetsLoaded;

    private SKBitmap? _policeIdleBitmap;
    private SKBitmap?[] _policeRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _policeArrestBitmap;
    private SKBitmap? _robberIdleBitmap;
    private SKBitmap?[] _robberRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _robberSurrendBitmap;
    private SKBitmap? _robberPrisonBreakBitmap;
    private SKBitmap? _policeStationBitmap;
    private SKBitmap? _jailBitmap;
    private SKBitmap? _wallBitmap;
    private SKBitmap? _buildingBitmap;
    private SKBitmap? _houseBitmap;
    private SKBitmap? _treeBitmap;
    private SKBitmap? _pondBitmap;
    private SKBitmap? _bushBitmap;

    // 체포 상태 만료 시간 기록 (2초 유지용)
    private Dictionary<string, DateTime> _arrestVisualTimers = new();
    // 탈옥 직후 해방 동작을 잠시 표시합니다.
    private readonly Dictionary<string, DateTime> _jailBreakVisualTimers = new();
    // 화면 중앙 체포 텍스트 표시 목표 시간
    private DateTime _showArrestedTextUntil = DateTime.MinValue;

    // 부자연스러운 애니메이션과 진동을 막기 위해 좌우대칭을 맞춘 프레임 시퀀스 구성 (오른쪽이 두 번 흔들리는 문제 해결)
    private int[] _runFramePattern = { 0, 1, 2, 3, 5, 6, 7, 1 };
    private int _currentRunFrameIndex = 0;
    private float _animationTimer = 0f;
    private bool _isInitialized = false;
    private Dictionary<string, float> _jailBreakProgressByRescuer = new();
    private string _roomId = string.Empty;
    private string _gameType = string.Empty;
    private string _roomCode = string.Empty;
    private bool _isHost;
    private PlayerRole _selectedRole = PlayerRole.Robber;

    public string RoomId
    {
        set
        {
            _roomId = value ?? string.Empty;
            if (_player != null)
            {
                _player.RoomId = _roomId;
            }
        }
    }

    public string Role
    {
        set
        {
            if (Enum.TryParse<PlayerRole>(value, true, out var role))
            {
                _selectedRole = role;
                if (_player != null)
                {
                    _player.Role = role;
                }
            }
        }
    }

    public string GameType
    {
        set => _gameType = value ?? string.Empty;
    }

    public string RoomCode
    {
        set => _roomCode = value ?? string.Empty;
    }

    public string IsHost
    {
        set => _isHost = bool.TryParse(value, out var isHost) && isHost;
    }

    public GamePlay()
    {
        InitializeComponent();

        _gameMap = new GameMap();

        _player = new Player
        {
            Id = AuthSession.UserId ?? Preferences.Get("userId", null) ?? Guid.NewGuid().ToString(),
            Name = GetLocalName(),
            RoomId = _roomId,
            X = _gameMap.Width / 2f,
            Y = _gameMap.Height / 2f,
            Speed = 7f,
            Radius = 50f,
            Role = _selectedRole,
            Angle = 0f,
            IsMoving = false
        };
        _players.Add(_player.Id, _player);

        _canvas = new SKCanvasView();
        _canvas.EnableTouchEvents = true;
        _canvas.Touch += Canvas_Touch;
        _canvas.PaintSurface += Canvas_PaintSurface;

        // Canvas를 가장 뒤(Index 0)에 배치하여 XAML에 정의된 Label 등 UI보다 뒤에 그려지도록 합니다.
        Container.Children.Insert(0, _canvas);

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
        _timer.Tick += (s, e) =>
        {
            UpdateRemotePlayerInterpolation();
            UpdatePhysics();
            _canvas.InvalidateSurface();
            UpdateUI();
        };
        _timer.Start();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AuthSession.LoadAsync();
        if (!string.IsNullOrWhiteSpace(AuthSession.UserId) && _player.Id != AuthSession.UserId)
        {
            _players.Remove(_player.Id);
            _player.Id = AuthSession.UserId;
            _players[_player.Id] = _player;
        }
        _player.RoomId = _roomId;
        _player.Role = _selectedRole;
        _player.Name = GetLocalName();

        await LoadAssetsAsync();
        await InitializeNetworkAsync();
    }

    private string GetServerIpAddress()
    {
        return AuthSession.GameServerHost;
    }

    private async Task InitializeNetworkAsync()
    {
        _networkClient = new GameNetworkClient();

        _networkClient.OnInitialStateReceived += (players) =>
        {
            _players.Clear();
            _remotePlayerInterpolations.Clear();
            foreach (var p in players)
            {
                _players[p.Id] = p;
            }
            // _players[_player.Id] = _player;
            _player = _players[_player.Id]; // Test
            foreach (var remotePlayer in _players.Values.Where(p => p.Id != _player.Id))
            {
                ResetRemotePlayerInterpolation(remotePlayer);
            }
            _isInitialized = true;
        };

        _networkClient.OnPlayerJoined += (p) =>
        {
            if (p.Id != _player.Id)
            {
                _players[p.Id] = p;
                ResetRemotePlayerInterpolation(p);
            }
        };

        _networkClient.OnPlayerMoved += (p) =>
        {
            if (!_players.TryGetValue(p.Id, out var player))
            {
                _players[p.Id] = p;
                player = p;
            }

            player.RoomId = p.RoomId;
            player.X = p.X;
            player.Y = p.Y;
            player.Speed = p.Speed;
            player.Radius = p.Radius;
            player.Angle = p.Angle;
            player.IsMoving = p.IsMoving;
            player.Role = p.Role;
            if (!string.IsNullOrWhiteSpace(p.Name))
            {
                player.Name = p.Name;
            }

            if (p.Id == _player.Id)
            {
                _player = player;
                if (_player.Role == PlayerRole.Robber && IsInJail(_player.X, _player.Y))
                {
                    _activeTouchId = -1;
                }
            }
            else
            {
                ResetRemotePlayerInterpolation(player);
            }
        };

        _networkClient.OnPlayerMovementReceived += (movement) =>
        {
            if (!_players.TryGetValue(movement.Id, out var player))
            {
                return;
            }

            if (movement.Id == _player.Id)
            {
                movement.ApplyTo(player);
                _player = player;
                if (_player.Role == PlayerRole.Robber && IsInJail(_player.X, _player.Y))
                {
                    _activeTouchId = -1;
                }
            }
            else
            {
                AddRemoteMovementSnapshot(player, movement);
            }
        };

        _networkClient.OnPlayerLeft += (playerId) =>
        {
            _players.Remove(playerId);
            _remotePlayerInterpolations.Remove(playerId);
        };

        _networkClient.OnPlayerArrested += (policeId, robberId) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TriggerArrestVisuals(policeId, robberId);
            });
        };

        _networkClient.OnPlayerJailBroken += (syncData) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ApplyJailBreak(syncData);
            });
        };

        _networkClient.OnJailBreakProgressReceived += (syncData) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!string.IsNullOrWhiteSpace(syncData.RoomId) && syncData.RoomId != _roomId)
                {
                    return;
                }

                _jailBreakProgressByRescuer = syncData.ProgressByRescuer ?? new();
                _canvas.InvalidateSurface();
            });
        };

        _networkClient.OnGameStateReceived += (syncData) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _gamePhase = syncData.Phase;
                _remainingTime = syncData.GameTime;
                _winnerRole = syncData.WinnerRole;

                if (_gamePhase == GamePhase.Countdown)
                {
                    CenterMessageLabel.Text = syncData.CountdownTime > 0 ? syncData.CountdownTime.ToString() : "Start";
                }
                else if (_gamePhase == GamePhase.Playing)
                {
                    if (CenterMessageLabel.Text == "Start" || int.TryParse(CenterMessageLabel.Text, out _))
                    {
                        CenterMessageLabel.Text = "";
                    }
                    TimerLabel.Text = $"Timer : {_remainingTime}";
                }
                else if (_gamePhase == GamePhase.Ended)
                {
                    if (!_isGameOverTransitioning)
                    {
                        _isGameOverTransitioning = true;

                        TimerLabel.IsVisible = false;
                        TimerLabel.Text = "";

                        CenterMessageLabel.Text = "GameOver";
                        CenterMessageLabel.TextColor = Colors.Red;
                        CenterMessageLabel.VerticalOptions = LayoutOptions.Start;
                        CenterMessageLabel.Margin = new Thickness(0, 20, 0, 0);

                        await Task.Delay(3000);
                        StopGameClient();
                        await Shell.Current.GoToAsync(BuildGameOverRoute());
                    }
                }
                else if (_gamePhase == GamePhase.Rematching)
                {
                    if (!_isGameOverTransitioning)
                    {
                        _isGameOverTransitioning = true;
                        TimerLabel.IsVisible = false;
                        TimerLabel.Text = "";
                        CenterMessageLabel.Text = "Rematching";
                        CenterMessageLabel.TextColor = Colors.White;

                        await Task.Delay(1000);
                        StopGameClient();
                        await Shell.Current.GoToAsync(BuildRematchingRoute());
                    }
                }
            });
        };

        try
        {
            await _networkClient.ConnectAsync(
                GetServerIpAddress(),
                _player,
                AuthSession.SessionToken ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network Connection Error: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopGameClient();
    }

    private void StopGameClient()
    {
        _timer.Stop();
        _networkClient?.Disconnect();
        _networkClient = null;
        _remotePlayerInterpolations.Clear();
    }

    private string BuildGameOverRoute()
    {
        var roomId = Uri.EscapeDataString(_roomId);
        var role = Uri.EscapeDataString(_selectedRole.ToString());
        var gameType = Uri.EscapeDataString(_gameType);
        var roomCode = Uri.EscapeDataString(_roomCode);
        var isHost = _isHost.ToString().ToLowerInvariant();
        var winnerRole = Uri.EscapeDataString((_winnerRole ?? PlayerRole.Robber).ToString());
        var robbers = _players.Values.Where(player => player.Role == PlayerRole.Robber).ToList();
        var totalRobbers = robbers.Count;
        var capturedRobbers = _gameMap?.Jail == null
            ? 0
            : robbers.Count(player =>
                player.X >= _gameMap.Jail.LeftTop.X &&
                player.X <= _gameMap.Jail.RightBottom.X &&
                player.Y >= _gameMap.Jail.LeftTop.Y &&
                player.Y <= _gameMap.Jail.RightBottom.Y);

        return $"GameOver?roomId={roomId}&role={role}&gameType={gameType}&roomCode={roomCode}&isHost={isHost}&winnerRole={winnerRole}&remainingTime={_remainingTime}&capturedRobbers={capturedRobbers}&totalRobbers={totalRobbers}";
    }

    private string BuildRematchingRoute()
    {
        var role = Uri.EscapeDataString(_selectedRole.ToString());
        return $"GameMatching?role={role}";
    }

    private void UpdateUI()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_gameMap?.Jail != null)
            {
                var robbers = _players.Values.Where(p => p.Role == PlayerRole.Robber).ToList();
                int totalRobbers = robbers.Count;
                int jailedRobbers = robbers.Count(p =>
                    p.X >= _gameMap.Jail.LeftTop.X &&
                    p.X <= _gameMap.Jail.RightBottom.X &&
                    p.Y >= _gameMap.Jail.LeftTop.Y &&
                    p.Y <= _gameMap.Jail.RightBottom.Y);

                JailLabel.Text = $"Jail : {jailedRobbers}/{totalRobbers}";
            }
        });
    }

    private async Task LoadAssetsAsync()
    {
        if (_assetsLoaded)
        {
            return;
        }

        await _assetLoadLock.WaitAsync();
        try
        {
            if (_assetsLoaded)
            {
                return;
            }

            _policeIdleBitmap = await LoadBitmapAsync("char_police_v3.png");
            _robberIdleBitmap = await LoadBitmapAsync("char_robber_v3.png");
            _policeArrestBitmap = await LoadBitmapAsync("char_police_arrest_v3.png");
            _robberSurrendBitmap = await LoadBitmapAsync("char_robber_surrend_v3.png");
            _robberPrisonBreakBitmap = await LoadBitmapAsync("char_robber_prison_break_v3.png");
            _policeStationBitmap = await LoadBitmapAsync("police_station.png");
            _jailBitmap = await LoadBitmapAsync("jail_v2.png");
            _wallBitmap = await LoadBitmapAsync("wall.png");
            _buildingBitmap = await LoadBitmapAsync("building.png");
            _houseBitmap = await LoadBitmapAsync("house_v2.png");
            _treeBitmap = await LoadBitmapAsync("tree.png");
            _pondBitmap = await LoadBitmapAsync("pond_v2.png");
            _bushBitmap = await LoadBitmapAsync("bush.png");

            for (int i = 0; i < 8; i++)
            {
                _policeRunBitmaps[i] = await LoadBitmapAsync($"char_police_run_v3_{i + 1}.png");
                _robberRunBitmaps[i] = await LoadBitmapAsync($"char_robber_run_v3_{i + 1}.png");
            }

            _assetsLoaded = true;
        }
        finally
        {
            _assetLoadLock.Release();
        }
    }

    private static async Task<SKBitmap?> LoadBitmapAsync(string fileName)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load image '{fileName}': {ex}");
            return null;
        }
    }

    private void Canvas_Touch(object? sender, SKTouchEventArgs e)
    {
        if (_gamePhase != GamePhase.Playing)
        {
            e.Handled = true;
            return;
        }

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                if (_activeTouchId == -1)
                {
                    // 좌측 하단 영역인지 대략적으로 확인 (예: x < 화면 반)
                    // 지금은 화면 크기를 터치 이벤트에서 바로 알 수 없으므로, _joystickCenter 주변인지로 판별하거나 무조건 왼쪽 화면에서 조이스틱을 시작할 수 있습니다.
                    float screenWidth = (float)_canvas.Width * (float)DeviceDisplay.MainDisplayInfo.Density;
                    float screenHeight = (float)_canvas.Height * (float)DeviceDisplay.MainDisplayInfo.Density;
                    if (e.Location.X < screenWidth / 2f && e.Location.Y > screenHeight / 2f || screenWidth == 0)
                    {
                        _activeTouchId = e.Id;
                        _joystickCenter = e.Location;
                        _joystickThumb = e.Location;
                    }
                }
                break;

            case SKTouchAction.Moved:
                if (e.Id == _activeTouchId)
                {
                    var dx = e.Location.X - _joystickCenter.X;
                    var dy = e.Location.Y - _joystickCenter.Y;
                    var distance = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (distance <= _joystickRadius)
                    {
                        _joystickThumb = e.Location;
                    }
                    else
                    {
                        _joystickThumb = new SKPoint(
                            _joystickCenter.X + (dx / distance) * _joystickRadius,
                            _joystickCenter.Y + (dy / distance) * _joystickRadius
                        );
                    }
                }
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                if (e.Id == _activeTouchId)
                {
                    _activeTouchId = -1;
                }
                break;
        }
        e.Handled = true;
    }

    private void ResetRemotePlayerInterpolation(Player player)
    {
        if (player.Id == _player.Id)
        {
            _remotePlayerInterpolations.Remove(player.Id);
            return;
        }

        var state = new RemotePlayerInterpolationState();
        state.Snapshots.Add(RemoteMovementSnapshot.FromPlayer(player, DateTime.UtcNow));
        _remotePlayerInterpolations[player.Id] = state;
    }

    private void AddRemoteMovementSnapshot(Player player, PlayerMovementSync movement)
    {
        var receivedAt = DateTime.UtcNow;
        if (!_remotePlayerInterpolations.TryGetValue(player.Id, out var state))
        {
            state = new RemotePlayerInterpolationState();
            _remotePlayerInterpolations[player.Id] = state;
        }

        if (state.Snapshots.Count == 0 ||
            receivedAt - state.Snapshots[^1].ReceivedAt > RemoteSnapshotResetGap)
        {
            state.Snapshots.Clear();
            state.Snapshots.Add(RemoteMovementSnapshot.FromPlayer(
                player,
                receivedAt - RemoteInterpolationDelay));
        }

        state.Snapshots.Add(new RemoteMovementSnapshot(
            movement.X,
            movement.Y,
            movement.Angle,
            movement.IsMoving,
            receivedAt));

        while (state.Snapshots.Count > MaxRemoteMovementSnapshots)
        {
            state.Snapshots.RemoveAt(0);
        }
    }

    private void UpdateRemotePlayerInterpolation()
    {
        var renderAt = DateTime.UtcNow - RemoteInterpolationDelay;
        List<string>? missingPlayerIds = null;

        foreach (var (playerId, state) in _remotePlayerInterpolations)
        {
            if (!_players.TryGetValue(playerId, out var player))
            {
                missingPlayerIds ??= new List<string>();
                missingPlayerIds.Add(playerId);
                continue;
            }

            while (state.Snapshots.Count > 2 && state.Snapshots[1].ReceivedAt <= renderAt)
            {
                state.Snapshots.RemoveAt(0);
            }

            if (state.Snapshots.Count == 0)
            {
                continue;
            }

            if (state.Snapshots.Count == 1 || renderAt <= state.Snapshots[0].ReceivedAt)
            {
                ApplyRemoteSnapshot(player, state.Snapshots[0]);
                continue;
            }

            var toIndex = state.Snapshots.FindIndex(snapshot => snapshot.ReceivedAt >= renderAt);
            if (toIndex <= 0)
            {
                ApplyRemoteSnapshot(player, state.Snapshots[^1]);
                continue;
            }

            var from = state.Snapshots[toIndex - 1];
            var to = state.Snapshots[toIndex];
            var durationMilliseconds = (to.ReceivedAt - from.ReceivedAt).TotalMilliseconds;
            var progress = durationMilliseconds <= 0d
                ? 1f
                : (float)Math.Clamp(
                    (renderAt - from.ReceivedAt).TotalMilliseconds / durationMilliseconds,
                    0d,
                    1d);

            player.X = from.X + ((to.X - from.X) * progress);
            player.Y = from.Y + ((to.Y - from.Y) * progress);
            player.Angle = NormalizeDegrees(
                from.Angle + (ShortestAngleDifference(from.Angle, to.Angle) * progress));
            player.IsMoving = progress < 1f
                ? from.IsMoving || to.IsMoving
                : to.IsMoving;
        }

        if (missingPlayerIds != null)
        {
            foreach (var playerId in missingPlayerIds)
            {
                _remotePlayerInterpolations.Remove(playerId);
            }
        }
    }

    private static void ApplyRemoteSnapshot(Player player, RemoteMovementSnapshot snapshot)
    {
        player.X = snapshot.X;
        player.Y = snapshot.Y;
        player.Angle = snapshot.Angle;
        player.IsMoving = snapshot.IsMoving;
    }

    private void UpdatePhysics()
    {
        if (!_isInitialized || _gamePhase != GamePhase.Playing)
        {
            _player.IsMoving = false;
            return;
        }

        _player.IsMoving = false;
        var inputX = 0f;
        var inputY = 0f;

        // 체포 상태이면 이동 불가
        bool isArrestedOrArresting = _arrestVisualTimers.TryGetValue(_player.Id, out var freezeEnd) && DateTime.Now < freezeEnd;

        if (_activeTouchId != -1 && !isArrestedOrArresting)
        {
            var dx = _joystickThumb.X - _joystickCenter.X;
            var dy = _joystickThumb.Y - _joystickCenter.Y;

            // 이동 방향 계산
            if (_joystickRadius > 0)
            {
                var moveX = dx / _joystickRadius * _player.Speed;
                var moveY = dy / _joystickRadius * _player.Speed;
                inputX = Math.Clamp(dx / _joystickRadius, -1f, 1f);
                inputY = Math.Clamp(dy / _joystickRadius, -1f, 1f);

                // 조이스틱이 아주 약간이라도 움직이면 캐릭터가 바라보는 각도를 갱신
                if (Math.Abs(dx) > 0.1f || Math.Abs(dy) > 0.1f)
                {
                    _player.IsMoving = true;
                    // Atan2 결과를 Degree로 변환. 원본 이미지가 아래쪽을 보고 있으므로 90도를 빼서 보정
                    float angleRadians = (float)Math.Atan2(dy, dx);
                    _player.Angle = (angleRadians * 180f / (float)Math.PI) - 90f;
                }

                var newX = _player.X + moveX;
                var newY = _player.Y + moveY;

                // 맵 경계 충돌 처리
                if (newX - _player.Radius < 0) newX = _player.Radius;
                if (newX + _player.Radius > _gameMap.Width) newX = _gameMap.Width - _player.Radius;
                if (newY - _player.Radius < 0) newY = _player.Radius;
                if (newY + _player.Radius > _gameMap.Height) newY = _gameMap.Height - _player.Radius;

                // 벽을 따라 미끄러지도록 X축, Y축 각각 충돌 검사
                if (!IsColliding(newX, _player.Y, _player.Radius))
                {
                    _player.X = newX;
                }

                if (!IsColliding(_player.X, newY, _player.Radius))
                {
                    _player.Y = newY;
                }
            }
        }

        // Run global animation timer for all moving players
        _animationTimer += 0.016f; // approx 16ms per frame
        if (_animationTimer >= 0.1f) // 100ms per animation frame
        {
            _animationTimer -= 0.1f;
            _currentRunFrameIndex = (_currentRunFrameIndex + 1) % _runFramePattern.Length;
        }

        // Sync with server via UDP
        if (_networkClient != null)
        {
            var now = DateTime.Now;
            var syncInterval = _player.IsMoving
                ? MovingUdpSyncInterval
                : StoppedUdpSyncInterval;
            var movementStateChanged = _player.IsMoving != _lastSyncedIsMoving;

            if (movementStateChanged || now - _lastSyncTime >= syncInterval)
            {
                _networkClient.SendMoveUdp(_player.Id, inputX, inputY);
                _lastSyncTime = now;
                _lastSyncedIsMoving = _player.IsMoving;
            }
        }
    }

    private bool IsColliding(float x, float y, float radius)
    {
        // 경찰서와 감옥은 통과할 수 없는 건물이다.
        foreach (var building in _gameMap.Buildings)
        {
            if (building.Type is not ("PoliceStation" or "Jail"))
                continue;

            if (IsCircleCollidingWithBuilding(x, y, radius, building))
            {
                return true;
            }
        }

        _gameMap.GetNearbyObstacles(x, y, radius, _nearbyCollisionObstacles);
        foreach (var obs in _nearbyCollisionObstacles)
        {
            // 부쉬는 충돌 장애물이 아니라 자유롭게 드나드는 은신 영역이다.
            if (GameMap.IsBushObstacle(obs))
            {
                continue;
            }

            if (obs.Type == "Rect")
            {
                var rect = new SKRect(obs.LeftTop.X, obs.LeftTop.Y, obs.RightBottom.X, obs.RightBottom.Y);

                // 원의 중심점과 사각형 내 가장 가까운 점 찾기
                float closestX = Math.Max(rect.Left, Math.Min(x, rect.Right));
                float closestY = Math.Max(rect.Top, Math.Min(y, rect.Bottom));

                float distanceX = x - closestX;
                float distanceY = y - closestY;

                if ((distanceX * distanceX) + (distanceY * distanceY) < (radius * radius))
                {
                    return true;
                }
            }
            else if (obs.Type == "Circle")
            {
                float dx = x - obs.CenterX.X;
                float dy = y - obs.CenterX.Y;
                float radiusSum = radius + obs.Radius;

                if ((dx * dx) + (dy * dy) < (radiusSum * radiusSum))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsCircleCollidingWithBuilding(float x, float y, float radius, MapBuilding building)
    {
        float closestX = Math.Max(building.LeftTop.X, Math.Min(x, building.RightBottom.X));
        float closestY = Math.Max(building.LeftTop.Y, Math.Min(y, building.RightBottom.Y));

        float distanceX = x - closestX;
        float distanceY = y - closestY;

        return (distanceX * distanceX) + (distanceY * distanceY) < (radius * radius);
    }

    private void ResetJailBreakProgress()
    {
        _jailBreakProgressByRescuer.Clear();
    }

    private void Canvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var width = e.Info.Width;
        var height = e.Info.Height;

        Draw(canvas, width, height);
    }

    private void Draw(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColor.Parse("#555555"));

        canvas.Save();

        // 1. 카메라 설정 (World Space로 이동)
        // 화면의 중심이 플레이어를 따라다니게 캔버스를 이동시킴
        canvas.Translate(width / 2f - _player.X, height / 2f - _player.Y);

        var visibleWorldBounds = new SKRect(
            _player.X - (width / 2f) - RenderCullPadding,
            _player.Y - (height / 2f) - RenderCullPadding,
            _player.X + (width / 2f) + RenderCullPadding,
            _player.Y + (height / 2f) + RenderCullPadding);

        using (var mapPaint = new SKPaint { Color = SKColors.LightGray, Style = SKPaintStyle.Stroke, StrokeWidth = 10 })
        {
            canvas.DrawRect(0, 0, _gameMap.Width, _gameMap.Height, mapPaint);
        }

        DrawBuildings(canvas, visibleWorldBounds);
        DrawObstacles(canvas, visibleWorldBounds);

        DrawVisionOverlay(canvas);

        DrawPlayers(canvas);
        DrawJailForeground(canvas, visibleWorldBounds);
        DrawJailBreakProgressBar(canvas);

        canvas.Restore();

        // 3. UI 오버레이 렌더링 코드는 카메라 복구 후에 그림

        if (DateTime.Now < _showArrestedTextUntil)
        {
            using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 120);
            using var textPaint = new SKPaint
            {
                Color = SKColors.Red,
                IsAntialias = true,
            };
            string text = "Arrested";
            var textBounds = new SKRect();
            font.MeasureText(text, out textBounds);
            // 중앙에서 위쪽으로 배치 (캐릭터를 가리지 않도록 Y축 상향 이동)
            canvas.DrawText(text, (width - textBounds.Width) / 2f, (height + textBounds.Height) / 2f - 250f, SKTextAlign.Left, font, textPaint);
        }

        // 조이스틱
        if (_activeTouchId != -1)
        {
            using var joystickPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 100), // 반투명 흰색
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            // 배경
            canvas.DrawCircle(_joystickCenter.X, _joystickCenter.Y, _joystickRadius, joystickPaint);

            // 썸(Thumb)
            joystickPaint.Color = new SKColor(255, 255, 255, 180);
            canvas.DrawCircle(_joystickThumb.X, _joystickThumb.Y, _thumbRadius, joystickPaint);
        }
        else
        {
            // 기본 상태 (고정된 위치에 조이스틱 그리기 - 좌측 하단)
            var defaultCenterX = _joystickRadius + 50f;
            var defaultCenterY = height - _joystickRadius - 50f;

            // 초기화 전이면 그리지 않거나 기본값 설정
            if (height > 0)
            {
                using var joystickPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, 50), // 더 연한 반투명
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawCircle(defaultCenterX, defaultCenterY, _joystickRadius, joystickPaint);

                joystickPaint.Color = new SKColor(255, 255, 255, 100);
                canvas.DrawCircle(defaultCenterX, defaultCenterY, _thumbRadius, joystickPaint);

                // 터치를 놓았을 때 화면 하단에 고정된 위치부터 시작하게 하려면
                // _joystickCenter를 터치 시 재설정하지 않고 고정 위치를 사용하도록 변경할 수 있음. 
                // 여기서는 터치한 곳 위치에 조이스틱이 뜨도록 (Floating Joystick) 구현함.
            }
        }
    }

    private void DrawPlayers(SKCanvas canvas)
    {
        foreach (var player in _players.Values)
        {
            var containingBush = _gameMap.FindBushContainingPoint(player.X, player.Y);
            var isInsideBush = containingBush != null;

            // 부쉬 밖에서는 안쪽의 다른 플레이어가 보이지 않는다. 같은 부쉬 안에서는 다시 보인다.
            if (player.Id != _player.Id &&
                containingBush != null &&
                !GameMap.ContainsPoint(containingBush, _player.X, _player.Y))
            {
                continue;
            }

            // 플레이어 렌더링
            SKBitmap? currentBitmap = null;
            bool isArrested = _arrestVisualTimers.TryGetValue(player.Id, out var arrestEnd) && DateTime.Now < arrestEnd;
            bool isJailBreaking = _jailBreakVisualTimers.TryGetValue(player.Id, out var jailBreakEnd) && DateTime.Now < jailBreakEnd;

            if (!isJailBreaking && jailBreakEnd != default)
            {
                _jailBreakVisualTimers.Remove(player.Id);
            }

            if (isArrested)
            {
                currentBitmap = player.Role == PlayerRole.Police ? _policeArrestBitmap : _robberSurrendBitmap;
            }
            else if (isJailBreaking && player.Role == PlayerRole.Robber)
            {
                currentBitmap = _robberPrisonBreakBitmap;
            }
            else if (player.Role == PlayerRole.Police)
            {
                currentBitmap = player.IsMoving ? _policeRunBitmaps[_runFramePattern[_currentRunFrameIndex]] : _policeIdleBitmap;
            }
            else if (player.Role == PlayerRole.Robber)
            {
                currentBitmap = player.IsMoving ? _robberRunBitmaps[_runFramePattern[_currentRunFrameIndex]] : _robberIdleBitmap;
            }

            if (currentBitmap != null)
            {
                canvas.Save();
                canvas.Translate(player.X, player.Y);
                canvas.RotateDegrees(player.Angle);

                float drawRadius = player.Radius * 2f; // 100f (기본 렌더링 범위: 200x200)

                // 모든 v3 캐릭터 이미지는 같은 512x512 캔버스와 중심점으로 정규화되어 있습니다.
                var destRect = new SKRect(-drawRadius, -drawRadius, drawRadius, drawRadius);
                if (isInsideBush)
                {
                    using var bushPaint = new SKPaint
                    {
                        Color = SKColors.White.WithAlpha(BushPlayerOpacity),
                        IsAntialias = true
                    };
                    canvas.DrawBitmap(currentBitmap, destRect, bushPaint);
                }
                else
                {
                    canvas.DrawBitmap(currentBitmap, destRect);
                }

                canvas.Restore();
            }
            else
            {
                using var paint = new SKPaint
                {
                    Color = (player.Role == PlayerRole.Police ? SKColors.Blue : SKColors.Red)
                        .WithAlpha(isInsideBush ? BushPlayerOpacity : byte.MaxValue),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawCircle(player.X, player.Y, player.Radius, paint);
            }

            DrawPlayerName(canvas, player);
        }
    }

    private static string GetLocalName()
    {
        return AuthSession.Name
            ?? Preferences.Get("name", null)
            ?? "Player";
    }

    private static void DrawPlayerName(SKCanvas canvas, Player player)
    {
        var name = string.IsNullOrWhiteSpace(player.Name) ? "Player" : player.Name.Trim();
        using var typeface = SKTypeface.FromFamilyName(
            "Arial",
            SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, PlayerNameFontSize);

        name = FitTextToWidth(name, font, PlayerNameMaxWidth);

        var y = player.Y - (player.Radius * 2.4f);

        using var outlinePaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 210),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5f
        };
        using var fillPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        canvas.DrawText(name, player.X, y, SKTextAlign.Center, font, outlinePaint);
        canvas.DrawText(name, player.X, y, SKTextAlign.Center, font, fillPaint);
    }

    private static string FitTextToWidth(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            return text;
        }

        const string suffix = "...";
        var maxLength = Math.Min(text.Length, 24);

        for (var length = maxLength; length > 0; length--)
        {
            var candidate = text[..length] + suffix;
            if (font.MeasureText(candidate) <= maxWidth)
            {
                return candidate;
            }
        }

        return suffix;
    }

    private void DrawBuildings(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        foreach (var building in _gameMap.Buildings)
        {
            var rect = new SKRect(
                building.LeftTop.X,
                building.LeftTop.Y,
                building.RightBottom.X,
                building.RightBottom.Y);

            if (!RectsIntersect(rect, visibleWorldBounds))
            {
                continue;
            }

            var bitmap = building.Type switch
            {
                "PoliceStation" => _policeStationBitmap,
                "Jail" => _jailBitmap,
                _ => null
            };

            if (bitmap != null)
            {
                canvas.DrawBitmap(bitmap, rect);
            }
        }
    }

    private void DrawJailForeground(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        if (_jailBitmap == null)
        {
            return;
        }

        var jail = _gameMap.Jail;
        var rect = new SKRect(
            jail.LeftTop.X,
            jail.LeftTop.Y,
            jail.RightBottom.X,
            jail.RightBottom.Y);

        if (!RectsIntersect(rect, visibleWorldBounds))
        {
            return;
        }

        // 플레이어 위에 쇠창살을 다시 그려 감옥 안에 갇혀 있는 깊이감을 만든다.
        using var foregroundPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(235),
            IsAntialias = true
        };
        canvas.DrawBitmap(_jailBitmap, rect, foregroundPaint);
    }

    private void DrawObstacles(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        using var fallbackPaint = new SKPaint
        {
            Color = SKColors.AliceBlue,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        foreach (var obstacle in _gameMap.Obstacles)
        {
            SKRect collisionRect;
            if (obstacle.Type == "Rect")
            {
                collisionRect = new SKRect(
                    obstacle.LeftTop.X,
                    obstacle.LeftTop.Y,
                    obstacle.RightBottom.X,
                    obstacle.RightBottom.Y);
            }
            else if (obstacle.Type == "Circle")
            {
                collisionRect = new SKRect(
                    obstacle.CenterX.X - obstacle.Radius,
                    obstacle.CenterX.Y - obstacle.Radius,
                    obstacle.CenterX.X + obstacle.Radius,
                    obstacle.CenterX.Y + obstacle.Radius);
            }
            else
            {
                continue;
            }

            var imageScale = obstacle.ImageFileName switch
            {
                // tree.png 자체의 좌우 투명 여백을 감안해 실제 수관이 충돌 원보다 살짝 크게 보이게 한다.
                "tree.png" => 1.70f,
                "bush.png" => 1.15f,
                _ => 1f
            };
            var imageRect = ScaleRectFromCenter(collisionRect, imageScale);

            if (!RectsIntersect(imageRect, visibleWorldBounds))
            {
                continue;
            }

            var bitmap = obstacle.ImageFileName switch
            {
                "wall.png" => _wallBitmap,
                "building.png" => _buildingBitmap,
                "house_v2.png" => _houseBitmap,
                "tree.png" => _treeBitmap,
                "pond_v2.png" => _pondBitmap,
                "bush.png" => _bushBitmap,
                _ => null
            };

            if (obstacle.Type == "Rect")
            {
                if (bitmap != null)
                {
                    canvas.DrawBitmap(bitmap, imageRect);
                }
                else
                {
                    canvas.DrawRect(collisionRect, fallbackPaint);
                }
            }
            else if (obstacle.Type == "Circle")
            {
                if (bitmap != null)
                {
                    canvas.DrawBitmap(bitmap, imageRect);
                }
                else
                {
                    canvas.DrawCircle(obstacle.CenterX.X, obstacle.CenterX.Y, obstacle.Radius, fallbackPaint);
                }
            }
        }
    }

    private static SKRect ScaleRectFromCenter(SKRect rect, float scale)
    {
        if (scale == 1f)
        {
            return rect;
        }

        var halfWidth = rect.Width * scale / 2f;
        var halfHeight = rect.Height * scale / 2f;
        return new SKRect(
            rect.MidX - halfWidth,
            rect.MidY - halfHeight,
            rect.MidX + halfWidth,
            rect.MidY + halfHeight);
    }

    private static bool RectsIntersect(SKRect first, SKRect second) =>
        first.Left <= second.Right &&
        first.Right >= second.Left &&
        first.Top <= second.Bottom &&
        first.Bottom >= second.Top;

    private void DrawJailBreakProgressBar(SKCanvas canvas)
    {
        if (_player.Role != PlayerRole.Robber || _jailBreakProgressByRescuer.Count == 0)
        {
            return;
        }

        var jail = _gameMap.Jail;
        float barWidth = Math.Min(jail.Width * 0.8f, 520f);
        float barHeight = 28f;
        float barGap = 10f;
        float barX = jail.Center.X - barWidth / 2f;
        float cornerRadius = 8f;
        var progressValues = _jailBreakProgressByRescuer
            .Where(kv => kv.Value > 0f)
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .ToList();

        if (progressValues.Count == 0)
        {
            return;
        }

        float totalHeight = (progressValues.Count * barHeight) + ((progressValues.Count - 1) * barGap);
        float firstBarY = jail.LeftTop.Y - 24f - totalHeight;

        using var backgroundPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 150),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var fillPaint = new SKPaint
        {
            Color = SKColors.Gold,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var borderPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f
        };

        for (int i = 0; i < progressValues.Count; i++)
        {
            float barY = firstBarY + (i * (barHeight + barGap));
            var backgroundRect = new SKRect(barX, barY, barX + barWidth, barY + barHeight);
            var fillRect = new SKRect(barX, barY, barX + (barWidth * progressValues[i]), barY + barHeight);

            canvas.DrawRoundRect(backgroundRect, cornerRadius, cornerRadius, backgroundPaint);
            canvas.DrawRoundRect(fillRect, cornerRadius, cornerRadius, fillPaint);
            canvas.DrawRoundRect(backgroundRect, cornerRadius, cornerRadius, borderPaint);
        }
    }

    private void DrawVisionOverlay(SKCanvas canvas)
    {
        using var fogPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, FogOpacity),
            Style = SKPaintStyle.Fill
        };

        using var clearPaint = new SKPaint
        {
            BlendMode = SKBlendMode.Clear,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        using var visionPath = CreateVisionPath(_player);

        canvas.SaveLayer();
        canvas.DrawRect(0, 0, _gameMap.Width, _gameMap.Height, fogPaint);
        canvas.DrawPath(visionPath, clearPaint);
        canvas.Restore();
    }

    private bool IsInJail(float x, float y)
    {
        var jail = _gameMap.Jail;
        return x >= jail.LeftTop.X && x <= jail.RightBottom.X &&
               y >= jail.LeftTop.Y && y <= jail.RightBottom.Y;
    }

    private SKPath CreateVisionPath(Player player)
    {
        float visionRange = GetVisionRange(player);
        float startAngle = GetFacingAngle(player) - VisionConeAngleDegrees / 2f;

        var path = new SKPath();
        var arcBounds = new SKRect(
            player.X - visionRange,
            player.Y - visionRange,
            player.X + visionRange,
            player.Y + visionRange);

        path.MoveTo(player.X, player.Y);
        path.LineTo(
            player.X + MathF.Cos(DegreesToRadians(startAngle)) * visionRange,
            player.Y + MathF.Sin(DegreesToRadians(startAngle)) * visionRange);
        path.ArcTo(arcBounds, startAngle, VisionConeAngleDegrees, false);
        path.Close();

        return path;
    }

    private static float GetVisionRange(Player player)
    {
        return player.Radius * 2f * VisionRangePlayerSizeMultiplier;
    }

    private static float GetFacingAngle(Player player)
    {
        return NormalizeDegrees(player.Angle + 90f);
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180f;
    }

    private static float NormalizeDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees < 0)
        {
            degrees += 360f;
        }

        return degrees;
    }

    private static float ShortestAngleDifference(float fromDegrees, float toDegrees)
    {
        float difference = NormalizeDegrees(toDegrees - fromDegrees);
        return difference > 180f ? difference - 360f : difference;
    }

    private void ApplyJailBreak(JailBreakSync syncData)
    {
        if (!_players.TryGetValue(syncData.RobberId, out var robber))
        {
            return;
        }

        robber.X = syncData.X;
        robber.Y = syncData.Y;
        robber.Angle = 0f;
        robber.IsMoving = false;

        if (robber.Id != _player.Id)
        {
            ResetRemotePlayerInterpolation(robber);
        }

        _arrestVisualTimers.Remove(syncData.RobberId);
        _jailBreakVisualTimers[syncData.RobberId] = DateTime.Now.AddSeconds(2);

        if (_player.Id == syncData.RobberId)
        {
            _activeTouchId = -1;
            _player.X = syncData.X;
            _player.Y = syncData.Y;
            _player.Angle = 0f;
            _player.IsMoving = false;
        }

        ResetJailBreakProgress();
        _canvas.InvalidateSurface();
    }

    private sealed class RemotePlayerInterpolationState
    {
        public List<RemoteMovementSnapshot> Snapshots { get; } = new();
    }

    private readonly record struct RemoteMovementSnapshot(
        float X,
        float Y,
        float Angle,
        bool IsMoving,
        DateTime ReceivedAt)
    {
        public static RemoteMovementSnapshot FromPlayer(Player player, DateTime receivedAt)
        {
            return new RemoteMovementSnapshot(
                player.X,
                player.Y,
                player.Angle,
                player.IsMoving,
                receivedAt);
        }
    }

    private void TriggerArrestVisuals(string policeId, string robberId)
    {
        var endTime = DateTime.Now.AddSeconds(2);
        _arrestVisualTimers[policeId] = endTime;
        _arrestVisualTimers[robberId] = endTime;
        _jailBreakVisualTimers.Remove(robberId);

        if (_player.Id == policeId || _player.Id == robberId)
        {
            _showArrestedTextUntil = endTime;
            _activeTouchId = -1;
        }
    }
}
