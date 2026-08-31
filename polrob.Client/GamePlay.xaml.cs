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
    private int _totalRobberCount;
    private int _jailedRobberCount;
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
    private const float PlayerBodyVisualWidthRatio = 0.86f;
    private const float TerrainTileWorldSize = 256f;
    private const float ForestGroundTop = 12f * TerrainTileWorldSize;
    private const int StreetTileCount = 7;
    private static readonly SKColor MapOutsideColor = SKColor.Parse("#4C4F4A");
    private static readonly SKColor MissingTerrainColor = SKColor.Parse("#7F865F");
    private static readonly SKRect VillageTerrainBounds = new(256f, 1280f, 4352f, 4352f);
    private static readonly SKRect WestForestTerrainBounds = new(0f, 4608f, 2816f, 7500f);
    private static readonly SKRect EastForestTerrainBounds = new(2304f, 4608f, 5000f, 7500f);
    private static readonly SKRect PondTerrainBounds = new(512f, 4864f, 2048f, 7424f);
    private readonly List<Obstacle> _nearbyCollisionObstacles = new();
    private readonly SemaphoreSlim _assetLoadLock = new(1, 1);
    private bool _assetsLoaded;
    private readonly ProximityHaptics _proximityHaptics = new();
    private int _proximityVibrationPulseMilliseconds;
    private long _nextProximityVibrationAt;

    private SKBitmap? _policeIdleBitmap;
    private SKBitmap?[] _policeRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _policeArrestBitmap;
    private SKBitmap? _robberIdleBitmap;
    private SKBitmap?[] _robberRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _robberSurrendBitmap;
    private SKBitmap? _robberPrisonBreakBitmap;
    private SKBitmap? _cityGroundBitmap;
    private SKBitmap? _forestGroundBitmap;
    private readonly SKBitmap?[] _streetBitmaps = new SKBitmap?[StreetTileCount];
    private readonly SKBitmap?[,] _terrainTiles = new SKBitmap?[4, 4];
    private readonly Dictionary<SKBitmap, SKRect> _spriteVisibleBounds = new();
    private readonly Dictionary<string, SKBitmap?> _mapPropBitmaps = new(StringComparer.Ordinal);
    private static readonly float[] PoliceRunBodyWidths = [475f, 470f, 466f, 464f, 466f, 470f, 475f, 470f];
    private static readonly float[] RobberRunBodyWidths = [418f, 409f, 402f, 411f, 402f, 409f, 418f, 409f];

    // The Canva reference is a 10 x 15 grid of 256 px cells. Rotations are
    // clockwise in the screen coordinate system used by SkiaSharp.
    private static readonly RoadTilePlacement[] RoadTilePlacements =
    [
        new(2, 1, 1, 0),
        new(2, 2, 3, 0),
        new(2, 3, 1, 90),
        new(2, 4, 1, 90),
        new(2, 5, 4, 0),
        new(2, 6, 1, 90),
        new(2, 7, 1, 90),
        new(2, 8, 3, 90),

        new(3, 1, 3, 270),
        new(3, 2, 5, 0),
        new(3, 5, 1, 0),
        new(3, 8, 1, 0),

        new(4, 2, 1, 0),
        new(4, 5, 1, 0),
        new(4, 8, 1, 0),

        new(5, 0, 3, 0),
        new(5, 1, 1, 90),
        new(5, 2, 2, 90),
        new(5, 5, 1, 0),
        new(5, 8, 1, 0),

        new(6, 0, 1, 0),
        new(6, 5, 1, 0),
        new(6, 8, 1, 0),

        new(7, 0, 4, 270),
        new(7, 1, 1, 90),
        new(7, 2, 1, 90),
        new(7, 3, 1, 90),
        new(7, 4, 1, 90),
        new(7, 5, 7, 0),
        new(7, 6, 1, 90),
        new(7, 7, 1, 90),
        new(7, 8, 4, 90),

        new(8, 0, 1, 0),
        new(8, 5, 1, 0),
        new(8, 8, 1, 0),
        new(9, 0, 1, 0),
        new(9, 5, 1, 0),
        new(9, 8, 1, 0),
        new(10, 0, 1, 0),
        new(10, 5, 1, 0),
        new(10, 8, 1, 0),

        new(11, 0, 3, 270),
        new(11, 1, 1, 90),
        new(11, 2, 1, 90),
        new(11, 3, 1, 90),
        new(11, 4, 1, 90),
        new(11, 5, 7, 0),
        new(11, 6, 1, 90),
        new(11, 7, 1, 90),
        new(11, 8, 2, 90),

        new(12, 5, 1, 0),
        new(13, 5, 1, 0),
        new(14, 5, 1, 0)
    ];

    private static readonly MapPropLayout[] MapPropPlacements = GameMap.PropLayouts;

    // 체포 상태 만료 시간 기록 (2초 유지용)
    private Dictionary<string, DateTime> _arrestVisualTimers = new();
    // 탈옥 직후 해방 동작을 잠시 표시합니다.
    private readonly Dictionary<string, DateTime> _jailBreakVisualTimers = new();
    // 화면 중앙 체포 텍스트 표시 목표 시간
    private DateTime _showArrestedTextUntil = DateTime.MinValue;

    // Four arm-swing poses play forward and then backward, keeping the
    // top-down body fixed while motion is communicated only by the arms.
    private int[] _runFramePattern = { 0, 1, 2, 3, 4, 5, 6, 7 };
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
        if (_gameMap.Width != 10f * TerrainTileWorldSize ||
            _gameMap.Height != 15f * TerrainTileWorldSize)
        {
            throw new InvalidOperationException(
                $"Road grid and shared world disagree: " +
                $"tiles={10f * TerrainTileWorldSize}x{15f * TerrainTileWorldSize}, " +
                $"world={_gameMap.Width}x{_gameMap.Height}.");
        }

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
            UpdateProximityVibration();
            _canvas.InvalidateSurface();
            UpdateUI();
        };
        _timer.Start();

        InitializeTeamVoiceControls();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BeginTeamVoiceLifetime();
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
        ScheduleTeamVoiceRosterRefresh();

        await LoadAssetsAsync();
        await InitializeNetworkAsync();
        // 보이스 토큰은 서버가 게임방 참가자를 확인한 뒤에만 발급되므로
        // 게임 네트워크 입장이 끝난 다음 팀 보이스에 연결합니다.
        await InitializeTeamVoiceAsync();
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
            ScheduleTeamVoiceRosterRefresh();
        };

        _networkClient.OnPlayerJoined += (p) =>
        {
            if (p.Id != _player.Id)
            {
                _players[p.Id] = p;
                ResetRemotePlayerInterpolation(p);
            }
            ScheduleTeamVoiceRosterRefresh();
        };

        _networkClient.OnPlayerMoved += (p) =>
        {
            var rosterChanged = false;
            if (!_players.TryGetValue(p.Id, out var player))
            {
                _players[p.Id] = p;
                player = p;
                rosterChanged = true;
            }

            var previousRole = player.Role;
            var previousName = player.Name;
            player.RoomId = p.RoomId;
            player.X = p.X;
            player.Y = p.Y;
            player.Speed = p.Speed;
            player.Radius = p.Radius;
            player.Angle = p.Angle;
            player.IsMoving = p.IsMoving;
            player.IsJailed = p.IsJailed;
            player.Role = p.Role;
            if (!string.IsNullOrWhiteSpace(p.Name))
            {
                player.Name = p.Name;
            }

            rosterChanged = rosterChanged ||
                            previousRole != player.Role ||
                            !string.Equals(previousName, player.Name, StringComparison.Ordinal);

            if (p.Id == _player.Id)
            {
                _player = player;
                if (_player.IsJailed)
                {
                    _activeTouchId = -1;
                }
            }
            else
            {
                ResetRemotePlayerInterpolation(player);
            }

            if (rosterChanged)
            {
                ScheduleTeamVoiceRosterRefresh();
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
                if (_player.IsJailed)
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
            ScheduleTeamVoiceRosterRefresh();
        };

        _networkClient.OnOpponentProximityReceived += (syncData) =>
        {
            var pulseMilliseconds = OpponentProximitySync.NormalizePulseMilliseconds(
                syncData.PulseMilliseconds);
            if (_proximityVibrationPulseMilliseconds == pulseMilliseconds)
            {
                return;
            }

            StopProximityVibration();
            _proximityVibrationPulseMilliseconds = pulseMilliseconds;
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
                _totalRobberCount = Math.Max(0, syncData.TotalRobbers);
                _jailedRobberCount = Math.Clamp(
                    syncData.JailedRobbers,
                    0,
                    _totalRobberCount);
                JailLabel.Text = $"{_jailedRobberCount}/{_totalRobberCount}";

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
                    TimerLabel.Text = _remainingTime.ToString();
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
                        await StopTeamVoiceAsync();
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
                        await StopTeamVoiceAsync();
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

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        StopGameClient();
        await StopTeamVoiceAsync();
    }

    private void StopGameClient()
    {
        _timer.Stop();
        StopProximityVibration();
        _proximityVibrationPulseMilliseconds = 0;
        _networkClient?.Disconnect();
        _networkClient = null;
        _remotePlayerInterpolations.Clear();
    }

    private void UpdateProximityVibration()
    {
        if (!_isInitialized ||
            _gamePhase != GamePhase.Playing ||
            _proximityVibrationPulseMilliseconds == 0)
        {
            StopProximityVibration();
            return;
        }

        var now = Environment.TickCount64;
        if (now < _nextProximityVibrationAt)
        {
            return;
        }

        _proximityHaptics.PlayPulse(
            TimeSpan.FromMilliseconds(_proximityVibrationPulseMilliseconds));
        _nextProximityVibrationAt =
            now + _proximityVibrationPulseMilliseconds * 2L;
    }

    private void StopProximityVibration()
    {
        if (_nextProximityVibrationAt == 0)
        {
            return;
        }

        _proximityHaptics.Stop();
        _nextProximityVibrationAt = 0;
    }

    private string BuildGameOverRoute()
    {
        var roomId = Uri.EscapeDataString(_roomId);
        var role = Uri.EscapeDataString(_selectedRole.ToString());
        var gameType = Uri.EscapeDataString(_gameType);
        var roomCode = Uri.EscapeDataString(_roomCode);
        var isHost = _isHost.ToString().ToLowerInvariant();
        var winnerRole = Uri.EscapeDataString((_winnerRole ?? PlayerRole.Robber).ToString());
        var totalRobbers = _totalRobberCount;
        var capturedRobbers = _jailedRobberCount;

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
            JailLabel.Text = $"{_jailedRobberCount}/{_totalRobberCount}";
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

            _policeIdleBitmap = await LoadBitmapAsync("char_police.png");
            _robberIdleBitmap = await LoadBitmapAsync("char_robber.png");
            _policeArrestBitmap = await LoadBitmapAsync("char_police_arrest.png");
            _robberSurrendBitmap = await LoadBitmapAsync("char_robber_surrend.png");
            _robberPrisonBreakBitmap = await LoadBitmapAsync("char_robber_prison-break.png");
            _cityGroundBitmap = await LoadBitmapAsync("FloorTiles/city_ground.png");
            _forestGroundBitmap = await LoadBitmapAsync("FloorTiles/forest_ground.png");

            for (var index = 0; index < _streetBitmaps.Length; index++)
            {
                _streetBitmaps[index] = await LoadBitmapAsync($"FloorTiles/street-{index + 1}.png");
            }

            foreach (var assetPath in MapPropPlacements
                .Select(placement => placement.AssetPath)
                .Distinct(StringComparer.Ordinal))
            {
                _mapPropBitmaps[assetPath] = await LoadBitmapAsync(assetPath);
            }

            for (int i = 0; i < 8; i++)
            {
                _policeRunBitmaps[i] = await LoadBitmapAsync($"char_police_run_{i + 1}.png");
                _robberRunBitmaps[i] = await LoadBitmapAsync($"char_robber_run_{i + 1}.png");
            }

            _assetsLoaded = true;
        }
        finally
        {
            _assetLoadLock.Release();
        }
    }

    private async Task LoadTerrainTilesAsync()
    {
        for (var row = 0; row < _terrainTiles.GetLength(0); row++)
        {
            for (var column = 0; column < _terrainTiles.GetLength(1); column++)
            {
                _terrainTiles[row, column] = await LoadBitmapAsync($"TerrainTiles/terrain_tile_{row}{column}.png");
            }
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

        if (_activeTouchId != -1 && !isArrestedOrArresting && !_player.IsJailed)
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
        return _gameMap.IsMovementPositionBlocked(x, y, radius, _nearbyCollisionObstacles);
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
        canvas.Clear(MapOutsideColor);

        canvas.Save();

        // Keep the camera inside the new 10 x 15 tile map.
        var cameraX = ClampCameraCenter(_player.X, width, _gameMap.Width);
        var cameraY = ClampCameraCenter(_player.Y, height, _gameMap.Height);
        canvas.Translate(width / 2f - cameraX, height / 2f - cameraY);

        var visibleWorldBounds = new SKRect(
            cameraX - (width / 2f) - RenderCullPadding,
            cameraY - (height / 2f) - RenderCullPadding,
            cameraX + (width / 2f) + RenderCullPadding,
            cameraY + (height / 2f) + RenderCullPadding);

        DrawMapBackground(canvas, visibleWorldBounds);

        DrawMapProps(canvas, visibleWorldBounds);

        // 시야 암전은 캐릭터 아래에 유지한다.
        DrawVisionOverlay(canvas);

        DrawPlayers(canvas);
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

    private static float ClampCameraCenter(float target, float viewportSize, float worldSize)
    {
        if (viewportSize >= worldSize)
        {
            return worldSize / 2f;
        }

        var halfViewport = viewportSize / 2f;
        return Math.Clamp(target, halfViewport, worldSize - halfViewport);
    }

    private void DrawMapBackground(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        var mapBounds = new SKRect(0, 0, _gameMap.Width, _gameMap.Height);

        canvas.Save();
        canvas.ClipRect(mapBounds);
        var cityGroundBounds = new SKRect(0f, 0f, _gameMap.Width, ForestGroundTop);
        DrawTiledRect(
            canvas,
            cityGroundBounds,
            visibleWorldBounds,
            _cityGroundBitmap,
            MissingTerrainColor);

        var forestGroundBounds = new SKRect(0f, ForestGroundTop, _gameMap.Width, _gameMap.Height);
        DrawTiledRect(
            canvas,
            forestGroundBounds,
            visibleWorldBounds,
            _forestGroundBitmap,
            MissingTerrainColor);

        foreach (var placement in RoadTilePlacements)
        {
            DrawRoadTile(canvas, visibleWorldBounds, placement);
        }

        canvas.Restore();
    }

    private void DrawRoadTile(
        SKCanvas canvas,
        SKRect visibleWorldBounds,
        RoadTilePlacement placement)
    {
        var bitmapIndex = placement.AssetNumber - 1;
        if (bitmapIndex < 0 || bitmapIndex >= _streetBitmaps.Length)
        {
            return;
        }

        var bitmap = _streetBitmaps[bitmapIndex];
        if (bitmap == null)
        {
            return;
        }

        var left = placement.Column * TerrainTileWorldSize;
        var top = placement.Row * TerrainTileWorldSize;
        var destination = new SKRect(
            left,
            top,
            left + TerrainTileWorldSize,
            top + TerrainTileWorldSize);
        if (!RectsIntersect(destination, visibleWorldBounds))
        {
            return;
        }

        var halfTile = TerrainTileWorldSize / 2f;
        var localDestination = new SKRect(-halfTile, -halfTile, halfTile, halfTile);

        canvas.Save();
        canvas.Translate(destination.MidX, destination.MidY);
        canvas.RotateDegrees(placement.RotationDegrees);

        if (placement.AssetNumber is 2 or 3)
        {
            // The supplied corner PNGs are opaque asphalt squares. The Canva
            // layout uses a quarter-disc footprint, so clip away the single
            // convex outer corner while retaining the source artwork.
            var clipCenterX = placement.AssetNumber == 2 ? -halfTile : halfTile;
            using var cornerClip = new SKPath();
            cornerClip.AddCircle(clipCenterX, halfTile, TerrainTileWorldSize);
            canvas.ClipPath(cornerClip, SKClipOperation.Intersect, antialias: true);
        }

        canvas.DrawBitmap(bitmap, localDestination);
        canvas.Restore();
    }

    private void DrawMapProps(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        foreach (var placement in MapPropPlacements)
        {
            if (!_mapPropBitmaps.TryGetValue(placement.AssetPath, out var bitmap) || bitmap == null)
            {
                continue;
            }

            var destination = new SKRect(
                placement.CenterX - placement.Width / 2f,
                placement.CenterY - placement.Height / 2f,
                placement.CenterX + placement.Width / 2f,
                placement.CenterY + placement.Height / 2f);
            if (!RectsIntersect(destination, visibleWorldBounds))
            {
                continue;
            }

            canvas.DrawBitmap(bitmap, destination);
        }
    }

    private void DrawOuterTerrain(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        var forestTile = TerrainTile(0, 0);
        var waterTile = TerrainTile(0, 3);

        SKRect[] forestRegions =
        [
            new(0f, 0f, _gameMap.Width, 520f),
            new(0f, 0f, 300f, _gameMap.Height),
            new(_gameMap.Width - 340f, 0f, _gameMap.Width, _gameMap.Height),
            new(0f, _gameMap.Height - 420f, _gameMap.Width, _gameMap.Height)
        ];

        foreach (var region in forestRegions)
        {
            DrawTiledRect(canvas, region, visibleWorldBounds, forestTile, SKColor.Parse("#355222"));
        }

        SKRect[] waterRegions =
        [
            new(0f, 6050f, 470f, _gameMap.Height),
            new(_gameMap.Width - 500f, 6120f, _gameMap.Width, _gameMap.Height),
            new(430f, 7180f, 1700f, _gameMap.Height),
            new(3720f, 7180f, _gameMap.Width, _gameMap.Height)
        ];

        foreach (var region in waterRegions)
        {
            DrawTiledRect(canvas, region, visibleWorldBounds, waterTile, SKColor.Parse("#1D7794"));
        }
    }

    private void DrawGeneratedMapLots(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(430f, 1090f, 1510f, 2040f));
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(1450f, 880f, 2290f, 1660f));
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(2800f, 470f, 3700f, 1300f));
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(3780f, 650f, 4710f, 1550f));
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(3260f, 1850f, 4380f, 2950f));
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(430f, 3560f, 1420f, 4560f));
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(3400f, 4170f, 4470f, 5240f));
        DrawPavedLot(canvas, visibleWorldBounds, new SKRect(1980f, 2380f, 2760f, 3200f));

        DrawDirtLot(canvas, visibleWorldBounds, new SKRect(1580f, 1710f, 2360f, 2420f));
        DrawDirtLot(canvas, visibleWorldBounds, new SKRect(2570f, 5580f, 3700f, 6260f));
        DrawDirtLot(canvas, visibleWorldBounds, new SKRect(3910f, 3300f, 4590f, 3970f));
    }

    private static void DrawPavedLot(SKCanvas canvas, SKRect visibleWorldBounds, SKRect rect)
    {
        if (!RectsIntersect(rect, visibleWorldBounds))
        {
            return;
        }

        using var basePaint = new SKPaint
        {
            Color = SKColor.Parse("#B9AA91"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var insetPaint = new SKPaint
        {
            Color = SKColor.Parse("#9C9283"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var linePaint = new SKPaint
        {
            Color = new SKColor(118, 110, 98, 100),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f
        };

        canvas.DrawRoundRect(rect, 70f, 70f, basePaint);
        var inset = SKRect.Inflate(rect, -95f, -95f);
        canvas.DrawRoundRect(inset, 45f, 45f, insetPaint);

        for (var x = inset.Left + 90f; x < inset.Right; x += 90f)
        {
            canvas.DrawLine(x, inset.Top, x, inset.Bottom, linePaint);
        }

        for (var y = inset.Top + 90f; y < inset.Bottom; y += 90f)
        {
            canvas.DrawLine(inset.Left, y, inset.Right, y, linePaint);
        }
    }

    private static void DrawDirtLot(SKCanvas canvas, SKRect visibleWorldBounds, SKRect rect)
    {
        if (!RectsIntersect(rect, visibleWorldBounds))
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = SKColor.Parse("#BA9351"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var edgePaint = new SKPaint
        {
            Color = SKColor.Parse("#786442"),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 18f
        };

        canvas.DrawRoundRect(rect, 48f, 48f, paint);
        canvas.DrawRoundRect(rect, 48f, 48f, edgePaint);
    }

    private void DrawGeneratedRoadNetwork(SKCanvas canvas, SKRect visibleWorldBounds)
    {
        foreach (var path in GeneratedRoadPaths())
        {
            DrawRoad(canvas, path);
        }

        DrawCrosswalk(canvas, new SKPoint(1340f, 2090f), 0f);
        DrawCrosswalk(canvas, new SKPoint(2790f, 1830f), 0f);
        DrawCrosswalk(canvas, new SKPoint(3090f, 3310f), 90f);
        DrawCrosswalk(canvas, new SKPoint(1480f, 3920f), 90f);
        DrawCrosswalk(canvas, new SKPoint(3330f, 5350f), 0f);
        DrawCrosswalk(canvas, new SKPoint(2480f, 6550f), 90f);
    }

    private static List<SKPath> GeneratedRoadPaths()
    {
        var main = new SKPath();
        main.MoveTo(2520f, 450f);
        main.CubicTo(2180f, 920f, 2520f, 1360f, 2360f, 1880f);
        main.CubicTo(2140f, 2500f, 2620f, 2980f, 2420f, 3650f);
        main.CubicTo(2220f, 4310f, 2810f, 4960f, 2500f, 5700f);
        main.CubicTo(2250f, 6290f, 2520f, 6780f, 2630f, 7250f);

        var leftLoop = new SKPath();
        leftLoop.MoveTo(2360f, 1740f);
        leftLoop.CubicTo(1710f, 1480f, 1010f, 1480f, 650f, 1910f);
        leftLoop.CubicTo(260f, 2380f, 500f, 3060f, 1190f, 3260f);
        leftLoop.CubicTo(1810f, 3440f, 2290f, 3090f, 2420f, 2700f);

        var rightLoop = new SKPath();
        rightLoop.MoveTo(2520f, 1450f);
        rightLoop.CubicTo(3120f, 980f, 4080f, 1060f, 4410f, 1680f);
        rightLoop.CubicTo(4750f, 2320f, 4180f, 3090f, 3470f, 3150f);
        rightLoop.CubicTo(3000f, 3190f, 2690f, 2920f, 2500f, 2560f);

        var lowerLeft = new SKPath();
        lowerLeft.MoveTo(2380f, 4100f);
        lowerLeft.CubicTo(1780f, 3840f, 920f, 3910f, 520f, 4520f);
        lowerLeft.CubicTo(120f, 5120f, 730f, 5790f, 1420f, 5660f);
        lowerLeft.CubicTo(1970f, 5560f, 2350f, 5180f, 2520f, 4740f);

        var lowerRight = new SKPath();
        lowerRight.MoveTo(2600f, 4070f);
        lowerRight.CubicTo(3220f, 3820f, 4240f, 4040f, 4490f, 4710f);
        lowerRight.CubicTo(4770f, 5480f, 3950f, 6070f, 3250f, 5800f);
        lowerRight.CubicTo(2790f, 5620f, 2560f, 5200f, 2520f, 4780f);

        var bottom = new SKPath();
        bottom.MoveTo(930f, 7060f);
        bottom.CubicTo(1260f, 6510f, 1810f, 6280f, 2440f, 6550f);
        bottom.CubicTo(3040f, 6810f, 3630f, 6620f, 4140f, 6160f);

        return new List<SKPath> { main, leftLoop, rightLoop, lowerLeft, lowerRight, bottom };
    }

    private static void DrawRoad(SKCanvas canvas, SKPath path)
    {
        using var sidewalkPaint = new SKPaint
        {
            Color = SKColor.Parse("#B7AA95"),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 560f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        using var curbPaint = new SKPaint
        {
            Color = SKColor.Parse("#7F766A"),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 470f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        using var asphaltPaint = new SKPaint
        {
            Color = SKColor.Parse("#2F3438"),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 420f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        using var dashPaint = new SKPaint
        {
            Color = new SKColor(224, 165, 48, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 22f,
            StrokeCap = SKStrokeCap.Round,
            PathEffect = SKPathEffect.CreateDash(new[] { 82f, 92f }, 0f)
        };

        canvas.DrawPath(path, sidewalkPaint);
        canvas.DrawPath(path, curbPaint);
        canvas.DrawPath(path, asphaltPaint);
        canvas.DrawPath(path, dashPaint);
    }

    private static void DrawCrosswalk(SKCanvas canvas, SKPoint center, float rotationDegrees)
    {
        canvas.Save();
        canvas.Translate(center.X, center.Y);
        canvas.RotateDegrees(rotationDegrees);

        using var paint = new SKPaint
        {
            Color = new SKColor(240, 240, 230, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        for (var i = -2; i <= 2; i++)
        {
            var x = i * 54f;
            canvas.DrawRoundRect(new SKRect(x - 18f, -145f, x + 18f, 145f), 6f, 6f, paint);
        }

        canvas.Restore();
    }

    private SKBitmap? TerrainTile(int row, int column) => _terrainTiles[row, column];

    private static void DrawTerrainRegion(
        SKCanvas canvas,
        SKRect bounds,
        SKRect visibleWorldBounds,
        SKBitmap? fillTile,
        SKBitmap? verticalEdgeTile,
        SKBitmap? horizontalEdgeTile,
        SKColor fallbackColor,
        bool invertWaterEdges = false)
    {
        DrawTiledRect(canvas, bounds, visibleWorldBounds, fillTile, fallbackColor);

        var tileSize = TerrainTileWorldSize;
        var topEdge = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + tileSize);
        var bottomEdge = new SKRect(bounds.Left, bounds.Bottom - tileSize, bounds.Right, bounds.Bottom);
        var leftEdge = new SKRect(bounds.Left, bounds.Top, bounds.Left + tileSize, bounds.Bottom);
        var rightEdge = new SKRect(bounds.Right - tileSize, bounds.Top, bounds.Right, bounds.Bottom);

        DrawTiledRect(canvas, topEdge, visibleWorldBounds, horizontalEdgeTile, fallbackColor, flipY: invertWaterEdges);
        DrawTiledRect(canvas, bottomEdge, visibleWorldBounds, horizontalEdgeTile, fallbackColor, flipY: !invertWaterEdges);
        DrawTiledRect(canvas, leftEdge, visibleWorldBounds, verticalEdgeTile, fallbackColor, flipX: !invertWaterEdges);
        DrawTiledRect(canvas, rightEdge, visibleWorldBounds, verticalEdgeTile, fallbackColor, flipX: invertWaterEdges);
    }

    private static void DrawTiledRect(
        SKCanvas canvas,
        SKRect bounds,
        SKRect visibleWorldBounds,
        SKBitmap? tile,
        SKColor fallbackColor,
        bool flipX = false,
        bool flipY = false)
    {
        if (!RectsIntersect(bounds, visibleWorldBounds))
        {
            return;
        }

        if (tile == null)
        {
            using var fallbackPaint = new SKPaint
            {
                Color = fallbackColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };
            canvas.DrawRect(bounds, fallbackPaint);
            return;
        }

        var startX = bounds.Left + (MathF.Floor((visibleWorldBounds.Left - bounds.Left) / TerrainTileWorldSize) * TerrainTileWorldSize);
        var startY = bounds.Top + (MathF.Floor((visibleWorldBounds.Top - bounds.Top) / TerrainTileWorldSize) * TerrainTileWorldSize);

        canvas.Save();
        canvas.ClipRect(bounds);

        for (var y = startY; y < visibleWorldBounds.Bottom && y < bounds.Bottom; y += TerrainTileWorldSize)
        {
            for (var x = startX; x < visibleWorldBounds.Right && x < bounds.Right; x += TerrainTileWorldSize)
            {
                var destination = new SKRect(x, y, x + TerrainTileWorldSize, y + TerrainTileWorldSize);
                if (RectsIntersect(destination, visibleWorldBounds))
                {
                    DrawTerrainTile(canvas, tile, destination, flipX, flipY);
                }
            }
        }

        canvas.Restore();
    }

    private static void DrawTerrainTile(SKCanvas canvas, SKBitmap tile, SKRect destination, bool flipX, bool flipY)
    {
        if (!flipX && !flipY)
        {
            canvas.DrawBitmap(tile, destination);
            return;
        }

        canvas.Save();
        canvas.Translate(destination.MidX, destination.MidY);
        canvas.Scale(flipX ? -1f : 1f, flipY ? -1f : 1f);

        var flippedDestination = new SKRect(
            -destination.Width / 2f,
            -destination.Height / 2f,
            destination.Width / 2f,
            destination.Height / 2f);

        canvas.DrawBitmap(tile, flippedDestination);
        canvas.Restore();
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
            var spriteProfile = default(PlayerSpriteProfile);
            bool isArrested = _arrestVisualTimers.TryGetValue(player.Id, out var arrestEnd) && DateTime.Now < arrestEnd;
            bool isJailBreaking = _jailBreakVisualTimers.TryGetValue(player.Id, out var jailBreakEnd) && DateTime.Now < jailBreakEnd;

            if (!isJailBreaking && jailBreakEnd != default)
            {
                _jailBreakVisualTimers.Remove(player.Id);
            }

            if (isArrested)
            {
                currentBitmap = player.Role == PlayerRole.Police ? _policeArrestBitmap : _robberSurrendBitmap;
                spriteProfile = player.Role == PlayerRole.Police
                    ? new PlayerSpriteProfile(521f, 512f, 450f)
                    : new PlayerSpriteProfile(510f, 512f, 505f);
            }
            else if (isJailBreaking && player.Role == PlayerRole.Robber)
            {
                currentBitmap = _robberPrisonBreakBitmap;
                spriteProfile = new PlayerSpriteProfile(460f, 512f, 440f);
            }
            else if (player.Role == PlayerRole.Police)
            {
                if (player.IsMoving)
                {
                    var frameIndex = _runFramePattern[_currentRunFrameIndex];
                    currentBitmap = _policeRunBitmaps[frameIndex];
                    spriteProfile = new PlayerSpriteProfile(
                        PoliceRunBodyWidths[frameIndex],
                        313.5f,
                        297f);
                }
                else
                {
                    currentBitmap = _policeIdleBitmap;
                    spriteProfile = new PlayerSpriteProfile(562f, 512f, 500f);
                }
            }
            else if (player.Role == PlayerRole.Robber)
            {
                if (player.IsMoving)
                {
                    var frameIndex = _runFramePattern[_currentRunFrameIndex];
                    currentBitmap = _robberRunBitmaps[frameIndex];
                    spriteProfile = new PlayerSpriteProfile(
                        RobberRunBodyWidths[frameIndex],
                        313.5f,
                        270f);
                }
                else
                {
                    currentBitmap = _robberIdleBitmap;
                    spriteProfile = new PlayerSpriteProfile(527f, 512f, 510f);
                }
            }

            if (currentBitmap != null)
            {
                canvas.Save();
                canvas.Translate(player.X, player.Y);
                canvas.RotateDegrees(player.Angle);

                // Props and outstretched arms must not make the character body shrink.
                // Every state uses the same on-screen body width and a stable body pivot;
                // pose-specific artwork is allowed to extend beyond the collision circle.
                var sourceRect = GetVisibleSpriteBounds(currentBitmap);
                var destRect = CreatePlayerDestinationRect(
                    sourceRect,
                    spriteProfile,
                    player.Radius * 2f);
                if (isInsideBush)
                {
                    using var bushPaint = new SKPaint
                    {
                        Color = SKColors.White.WithAlpha(BushPlayerOpacity),
                        IsAntialias = true
                    };
                    canvas.DrawBitmap(currentBitmap, sourceRect, destRect, bushPaint);
                }
                else
                {
                    canvas.DrawBitmap(currentBitmap, sourceRect, destRect);
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

    private SKRect GetVisibleSpriteBounds(SKBitmap bitmap)
    {
        if (_spriteVisibleBounds.TryGetValue(bitmap, out var bounds))
        {
            return bounds;
        }

        const byte minimumVisibleAlpha = 32;
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha < minimumVisibleAlpha)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        bounds = right < left || bottom < top
            ? new SKRect(0f, 0f, bitmap.Width, bitmap.Height)
            : new SKRect(left, top, right + 1f, bottom + 1f);
        _spriteVisibleBounds[bitmap] = bounds;
        return bounds;
    }

    private static SKRect CreatePlayerDestinationRect(
        SKRect sourceRect,
        PlayerSpriteProfile profile,
        float playerDiameter)
    {
        var scale = playerDiameter * PlayerBodyVisualWidthRatio / profile.BodyWidthPixels;
        return new SKRect(
            (sourceRect.Left - profile.PivotX) * scale,
            (sourceRect.Top - profile.PivotY) * scale,
            (sourceRect.Right - profile.PivotX) * scale,
            (sourceRect.Bottom - profile.PivotY) * scale);
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

        // The sprite's maximum height equals the collision diameter; keep the
        // existing 20 world-unit gap between its top edge and the name.
        var y = player.Y - (player.Radius * 1.4f);

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
        robber.IsJailed = false;

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

    private readonly record struct RoadTilePlacement(
        int Row,
        int Column,
        int AssetNumber,
        float RotationDegrees);

    private readonly record struct PlayerSpriteProfile(
        float BodyWidthPixels,
        float PivotX,
        float PivotY);

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
