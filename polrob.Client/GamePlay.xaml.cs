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
public partial class GamePlay : ContentPage
{
    private Player _player;
    private Dictionary<string, Player> _players = new();
    // 감지된(현재 시야에 들어와 처리된) 도둑 목록
    private HashSet<string> _detectedRobbers = new();

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
    private int _gamePhase = 0; // 0=Wait, 1=Countdown, 2=Play, 3=End
    private int _remainingTime = 300;
    private bool _isGameOverTransitioning = false;
    private const float VisionRangePlayerSizeMultiplier = 2.5f;
    private const float VisionConeAngleDegrees = 90f;
    private const byte FogOpacity = 120;
    private const double JailBreakDurationSeconds = 3d;
    private const float JailBreakRemoteContactTolerance = 20f;

    // private SKBitmap? _playerIdleBitmap;
    // private SKBitmap?[] _playerRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _policeIdleBitmap;
    private SKBitmap?[] _policeRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _policeArrestBitmap;
    private SKBitmap? _robberIdleBitmap;
    private SKBitmap?[] _robberRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _robberSurrendBitmap;
    private SKBitmap? _policeStationBitmap;
    private SKBitmap? _jailBitmap;

    // 체포 상태 만료 시간 기록 (2초 유지용)
    private Dictionary<string, DateTime> _arrestVisualTimers = new();
    // 화면 중앙 체포 텍스트 표시 목표 시간
    private DateTime _showArrestedTextUntil = DateTime.MinValue;

    // 부자연스러운 애니메이션과 진동을 막기 위해 좌우대칭을 맞춘 프레임 시퀀스 구성 (오른쪽이 두 번 흔들리는 문제 해결)
    private int[] _runFramePattern = { 0, 1, 2, 3, 5, 6, 7, 1 };
    private int _currentRunFrameIndex = 0;
    private float _animationTimer = 0f;
    private bool _isInitialized = false;
    private Dictionary<string, DateTime> _jailBreakStartedAtByRescuer = new();
    private Dictionary<string, float> _jailBreakProgressByRescuer = new();
    private string _roomId = string.Empty;
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

    public GamePlay()
    {
        InitializeComponent();

        _gameMap = new GameMap();

        _player = new Player
        {
            Id = AuthSession.PlayerId ?? Preferences.Get("playerId", null) ?? Guid.NewGuid().ToString(),
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
        if (!string.IsNullOrWhiteSpace(AuthSession.PlayerId) && _player.Id != AuthSession.PlayerId)
        {
            _players.Remove(_player.Id);
            _player.Id = AuthSession.PlayerId;
            _players[_player.Id] = _player;
        }
        _player.RoomId = _roomId;
        _player.Role = _selectedRole;

        await LoadAssetsAsync();
        await InitializeNetworkAsync();
    }

    private string GetServerIpAddress()
    {
        // 시뮬레이터 2개 테스트를 위해 localhost(127.0.0.1)를 사용합니다.
        // 실제 기기(안드로이드, 아이폰) 연결 시에는 현재 로컬 IP(예: 192.0.0.2 등)로 변경해야 합니다.
        // 집 와이파이 : 192.168.0.238
        return "127.0.0.1";
    }

    private async Task InitializeNetworkAsync()
    {
        _networkClient = new GameNetworkClient();

        _networkClient.OnInitialStateReceived += async (players) =>
        {
            _players.Clear();
            foreach (var p in players)
            {
                _players[p.Id] = p;
            }
            // _players[_player.Id] = _player;
            _player = _players[_player.Id]; // Test
            await LoadAssetsAsync(); // Test
            _isInitialized = true;
        };

        _networkClient.OnPlayerJoined += (p) =>
        {
            if (p.Id != _player.Id) _players[p.Id] = p;
        };

        _networkClient.OnPlayerMoved += (p) =>
        {
            if (p.Id != _player.Id && _players.ContainsKey(p.Id))
            {
                _players[p.Id].X = p.X;
                _players[p.Id].Y = p.Y;
                _players[p.Id].Angle = p.Angle;
                _players[p.Id].IsMoving = p.IsMoving;
            }
        };

        _networkClient.OnPlayerLeft += (playerId) =>
        {
            _players.Remove(playerId);
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

        _networkClient.OnGameStateReceived += (syncData) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _gamePhase = syncData.Phase;
                _remainingTime = syncData.GameTime;

                if (_gamePhase == 1)
                {
                    CenterMessageLabel.Text = syncData.CountdownTime > 0 ? syncData.CountdownTime.ToString() : "Start";
                }
                else if (_gamePhase == 2)
                {
                    if (CenterMessageLabel.Text == "Start" || int.TryParse(CenterMessageLabel.Text, out _))
                    {
                        CenterMessageLabel.Text = "";
                    }
                    TimerLabel.Text = $"Timer : {_remainingTime}";
                }
                else if (_gamePhase == 3)
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
                        await Shell.Current.GoToAsync("GameOver");
                    }
                }
            });
        };

        try
        {
            await _networkClient.ConnectAsync(GetServerIpAddress(), _player);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network Connection Error: {ex}");
        }
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
        try
        {
            using var policeStream = await FileSystem.OpenAppPackageFileAsync($"char_police.png");
            _policeIdleBitmap = SKBitmap.Decode(policeStream);

            using var robberStream = await FileSystem.OpenAppPackageFileAsync($"char_robber.png");
            _robberIdleBitmap = SKBitmap.Decode(robberStream);

            using var policeArrestStream = await FileSystem.OpenAppPackageFileAsync($"char_police_arrest.png");
            _policeArrestBitmap = SKBitmap.Decode(policeArrestStream);

            using var robberSurrendStream = await FileSystem.OpenAppPackageFileAsync($"char_robber-surrend.png");
            _robberSurrendBitmap = SKBitmap.Decode(robberSurrendStream);

            using var policeStationStream = await FileSystem.OpenAppPackageFileAsync($"police_station.png");
            _policeStationBitmap = SKBitmap.Decode(policeStationStream);

            using var jailStream = await FileSystem.OpenAppPackageFileAsync($"jail.png");
            _jailBitmap = SKBitmap.Decode(jailStream);

            for (int i = 0; i < 8; i++)
            {
                using var policeRunStream = await FileSystem.OpenAppPackageFileAsync($"char_police_run_{i + 1}.png");
                _policeRunBitmaps[i] = SKBitmap.Decode(policeRunStream);

                using var robberRunStream = await FileSystem.OpenAppPackageFileAsync($"char_robber_run_{i + 1}.png");
                _robberRunBitmaps[i] = SKBitmap.Decode(robberRunStream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load image: {ex}");
        }
    }

    private void Canvas_Touch(object? sender, SKTouchEventArgs e)
    {
        if (_gamePhase < 2)
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

    private void UpdatePhysics()
    {
        if (!_isInitialized || _gamePhase < 2)
        {
            _player.IsMoving = false;
            return;
        }

        _player.IsMoving = false;

        // 체포 상태이면 이동 불가
        bool isArrestedOrArresting = _arrestVisualTimers.TryGetValue(_player.Id, out var freezeEnd) && DateTime.Now < freezeEnd;

        bool isTouchingJailForBreak = false;

        if (_activeTouchId != -1 && !isArrestedOrArresting)
        {
            var dx = _joystickThumb.X - _joystickCenter.X;
            var dy = _joystickThumb.Y - _joystickCenter.Y;

            // 이동 방향 계산
            if (_joystickRadius > 0)
            {
                var moveX = dx / _joystickRadius * _player.Speed;
                var moveY = dy / _joystickRadius * _player.Speed;

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
                bool isCollidingWithJailX = IsCollidingWithJail(newX, _player.Y, _player.Radius);
                if (!IsColliding(newX, _player.Y, _player.Radius))
                {
                    _player.X = newX;
                }

                bool isCollidingWithJailY = IsCollidingWithJail(_player.X, newY, _player.Radius);
                if (!IsColliding(_player.X, newY, _player.Radius))
                {
                    _player.Y = newY;
                }

                isTouchingJailForBreak = isCollidingWithJailX || isCollidingWithJailY;
            }
        }

        UpdateJailBreakProgress(isTouchingJailForBreak);

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
            if (_player.IsMoving || (DateTime.Now - _lastSyncTime).TotalMilliseconds > 100)
            {
                // Sync around 20 times a second
                if ((DateTime.Now - _lastSyncTime).TotalMilliseconds > 50)
                {
                    _networkClient.SendMoveUdp(_player);
                    _lastSyncTime = DateTime.Now;
                }
            }
        }

        // 감지 처리: 로컬 플레이어가 경찰이면 다른 플레이어들(도둑)을 시야 검사하여
        // 새로 감지된 경우 핸들러를 호출합니다.
        if (_player.Role == PlayerRole.Police)
        {
            foreach (var kv in _players)
            {
                var other = kv.Value;
                if (other.Id == _player.Id) continue;
                if (other.Role != PlayerRole.Robber) continue;

                bool inVision = IsPointInVision(other.X, other.Y);
                bool inJail = IsInJail(other.X, other.Y);

                // 감옥 밖에서 시야에 들어왔을 때 새로 발각 처리
                if (inVision && !inJail && !_detectedRobbers.Contains(other.Id))
                {
                    _detectedRobbers.Add(other.Id);
                    HandleRobberDetected(other);
                }
                else if ((!inVision || inJail) && _detectedRobbers.Contains(other.Id))
                {
                    // 시야에서 벗어나거나 기 체포(감옥 안) 상태가 되면 감지 상태 해제
                    _detectedRobbers.Remove(other.Id);
                }
            }
        }
    }

    private bool IsColliding(float x, float y, float radius)
    {
        // 건물(감옥) 충돌 처리 (경찰서는 통과 가능)
        foreach (var building in _gameMap.Buildings)
        {
            if (building.Type != "Jail")
                continue;

            if (IsCircleCollidingWithBuilding(x, y, radius, building))
            {
                return true;
            }
        }

        foreach (var obs in _gameMap.Obstacles)
        {
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

    private bool IsCollidingWithJail(float x, float y, float radius)
    {
        return IsCircleCollidingWithBuilding(x, y, radius, _gameMap.Jail);
    }

    private bool IsNearJailForBreak(Player player)
    {
        var jail = _gameMap.Jail;
        float closestX = Math.Max(jail.LeftTop.X, Math.Min(player.X, jail.RightBottom.X));
        float closestY = Math.Max(jail.LeftTop.Y, Math.Min(player.Y, jail.RightBottom.Y));
        float distanceX = player.X - closestX;
        float distanceY = player.Y - closestY;
        float allowedDistance = player.Radius + JailBreakRemoteContactTolerance;

        return (distanceX * distanceX) + (distanceY * distanceY) <= allowedDistance * allowedDistance;
    }

    private static bool IsCircleCollidingWithBuilding(float x, float y, float radius, MapBuilding building)
    {
        float closestX = Math.Max(building.LeftTop.X, Math.Min(x, building.RightBottom.X));
        float closestY = Math.Max(building.LeftTop.Y, Math.Min(y, building.RightBottom.Y));

        float distanceX = x - closestX;
        float distanceY = y - closestY;

        return (distanceX * distanceX) + (distanceY * distanceY) < (radius * radius);
    }

    private void UpdateJailBreakProgress(bool isLocalTouchingJailForBreak)
    {
        if (_player.Role != PlayerRole.Robber)
        {
            ResetJailBreakProgress();
            return;
        }

        int jailedRobberCount = _players.Values.Count(p => p.Role == PlayerRole.Robber && IsInJail(p.X, p.Y));
        if (jailedRobberCount == 0)
        {
            ResetJailBreakProgress();
            return;
        }

        var activeRescuers = new List<Player>();
        if (!IsInJail(_player.X, _player.Y) && isLocalTouchingJailForBreak)
        {
            activeRescuers.Add(_player);
        }

        activeRescuers.AddRange(_players.Values
            .Where(p => p.Id != _player.Id &&
                        p.Role == PlayerRole.Robber &&
                        p.IsMoving &&
                        !IsInJail(p.X, p.Y) &&
                        IsNearJailForBreak(p))
            .OrderBy(p => p.Id));
        activeRescuers = activeRescuers.Take(jailedRobberCount).ToList();

        if (activeRescuers.Count == 0)
        {
            ResetJailBreakProgress();
            return;
        }

        var now = DateTime.Now;
        var activeRescuerIds = activeRescuers.Select(p => p.Id).ToHashSet();
        foreach (var rescuerId in _jailBreakStartedAtByRescuer.Keys.Where(id => !activeRescuerIds.Contains(id)).ToList())
        {
            _jailBreakStartedAtByRescuer.Remove(rescuerId);
            _jailBreakProgressByRescuer.Remove(rescuerId);
        }

        foreach (var rescuer in activeRescuers)
        {
            if (!_jailBreakStartedAtByRescuer.TryGetValue(rescuer.Id, out var startedAt))
            {
                startedAt = now;
                _jailBreakStartedAtByRescuer[rescuer.Id] = startedAt;
            }

            double elapsedSeconds = (now - startedAt).TotalSeconds;
            _jailBreakProgressByRescuer[rescuer.Id] = Math.Clamp((float)(elapsedSeconds / JailBreakDurationSeconds), 0f, 1f);

            if (rescuer.Id == _player.Id && elapsedSeconds >= JailBreakDurationSeconds)
            {
                _networkClient?.SendJailBreakRequest(_player.Id);
                ResetJailBreakProgress();
                return;
            }
        }
    }

    private void ResetJailBreakProgress()
    {
        _jailBreakStartedAtByRescuer.Clear();
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

        // 2. 월드 렌더링
        using (var mapPaint = new SKPaint { Color = SKColors.LightGray, Style = SKPaintStyle.Stroke, StrokeWidth = 10 })
        {
            canvas.DrawRect(0, 0, _gameMap.Width, _gameMap.Height, mapPaint);
        }

        DrawBuildings(canvas);

        // 장애물 렌더링
        using (var obsPaint = new SKPaint { Color = SKColors.AliceBlue, Style = SKPaintStyle.Fill })
        {
            foreach (var obs in _gameMap.Obstacles)
            {
                if (obs.Type == "Rect")
                {
                    // 좌측상단 ~ 우측하단을 사용해 사각형을 그림
                    var rect = new SKRect(obs.LeftTop.X, obs.LeftTop.Y, obs.RightBottom.X, obs.RightBottom.Y);
                    canvas.DrawRect(rect, obsPaint);
                }
                else if (obs.Type == "Circle")
                {
                    canvas.DrawCircle(obs.CenterX.X, obs.CenterX.Y, obs.Radius, obsPaint);
                }
            }
        }

        DrawVisionOverlay(canvas);

        DrawJailBreakProgressBar(canvas);

        DrawPlayers(canvas);

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
            if (!ShouldDrawPlayer(player))
            {
                continue;
            }

            // 플레이어 렌더링
            SKBitmap? currentBitmap = null;
            bool isArrested = _arrestVisualTimers.TryGetValue(player.Id, out var arrestEnd) && DateTime.Now < arrestEnd;

            if (isArrested)
            {
                currentBitmap = player.Role == PlayerRole.Police ? _policeArrestBitmap : _robberSurrendBitmap;
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

                if (isArrested || !player.IsMoving || currentBitmap == _policeIdleBitmap || currentBitmap == _robberIdleBitmap)
                {
                    // Idle 이미지 (1024x1024)는 여백이 많으므로 200x200 박스에 렌더링합니다.
                    var destRect = new SKRect(-drawRadius, -drawRadius, drawRadius, drawRadius);
                    canvas.DrawBitmap(currentBitmap, destRect);
                }
                else
                {
                    // Run 이미지들은 여백 없이 타이트하게 크롭되어 있습니다. (약 280x315 크기)
                    // 200x200에 꽉 채우면 여백이 없어 원래 캐릭터보다 엄청 커 보이고, 전처럼 줄이면 너무 작아집니다.
                    // Idle 이미지 안의 실제 캐릭터 비율과 눈대중으로 일치하도록 맞춰줍니다.
                    float targetHeight = drawRadius * 1.35f; // 가만히 있을 때와 시각적으로 비슷한 높이 지정
                    float scale = targetHeight / currentBitmap.Height;
                    float scaledWidth = currentBitmap.Width * scale;
                    float scaledHeight = currentBitmap.Height * scale;

                    // 각각의 크롭된 이미지를 항상 중심 기준으로 그려 진동(Jitter)을 방지합니다.
                    var destRect = new SKRect(-scaledWidth / 2f, -scaledHeight / 2f, scaledWidth / 2f, scaledHeight / 2f);
                    canvas.DrawBitmap(currentBitmap, destRect);
                }

                canvas.Restore();
            }
            else
            {
                using var paint = new SKPaint
                {
                    Color = player.Role == PlayerRole.Police ? SKColors.Blue : SKColors.Red,
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawCircle(player.X, player.Y, player.Radius, paint);
            }
        }
    }

    private void DrawBuildings(SKCanvas canvas)
    {
        foreach (var building in _gameMap.Buildings)
        {
            var rect = new SKRect(
                building.LeftTop.X,
                building.LeftTop.Y,
                building.RightBottom.X,
                building.RightBottom.Y);

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

    private bool ShouldDrawPlayer(Player player)
    {
        if (player.Id == _player.Id)
        {
            return true;
        }

        if (player.Role == _player.Role)
        {
            return true;
        }

        if (player.Role == PlayerRole.Robber && IsInJail(player.X, player.Y))
        {
            return true;
        }

        // 체포 연출 중인 플레이어(경찰이든 도둑이든)는 양쪽 시야에 모두 무조건 표시됨
        if (_arrestVisualTimers.TryGetValue(player.Id, out var arrestEnd) && DateTime.Now < arrestEnd)
        {
            return true;
        }

        return IsPointInVision(player.X, player.Y);
    }

    private bool IsPointInVision(float x, float y)
    {
        float dx = x - _player.X;
        float dy = y - _player.Y;
        float distanceSquared = dx * dx + dy * dy;
        float visionRange = GetVisionRange(_player);

        if (distanceSquared > visionRange * visionRange)
        {
            return false;
        }

        float targetAngle = NormalizeDegrees((float)(Math.Atan2(dy, dx) * 180f / Math.PI));
        float facingAngle = GetFacingAngle(_player);
        float angleDifference = Math.Abs(ShortestAngleDifference(facingAngle, targetAngle));

        return angleDifference <= VisionConeAngleDegrees / 2f;
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

        _arrestVisualTimers.Remove(syncData.RobberId);
        _detectedRobbers.Remove(syncData.RobberId);

        if (_player.Id == syncData.RobberId)
        {
            _activeTouchId = -1;
            _player.X = syncData.X;
            _player.Y = syncData.Y;
            _player.Angle = 0f;
            _player.IsMoving = false;
            _networkClient?.SendMoveUdp(_player);
        }

        ResetJailBreakProgress();
        _canvas.InvalidateSurface();
    }

    private void TriggerArrestVisuals(string policeId, string robberId)
    {
        var endTime = DateTime.Now.AddSeconds(2);
        _arrestVisualTimers[policeId] = endTime;
        _arrestVisualTimers[robberId] = endTime;

        if (_player.Id == policeId || _player.Id == robberId)
        {
            _showArrestedTextUntil = endTime;
        }

        // 자신이 도둑일 경우 2초 뒤 감옥으로 자동 이동 처리
        if (_player.Id == robberId)
        {
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var jailCenter = _gameMap.Jail.Center;

                    var allRobbers = _players.Values.Where(p => p.Role == PlayerRole.Robber).OrderBy(p => p.Id).ToList();
                    int myIndex = allRobbers.FindIndex(p => p.Id == _player.Id);
                    if (myIndex == -1) myIndex = 0;

                    float gap = 150f;
                    float offsetX = (myIndex % 2 == 0) ? -gap / 2f : gap / 2f;
                    float offsetY = (myIndex < 2) ? -gap / 2f : gap / 2f;

                    _player.X = jailCenter.X + offsetX;
                    _player.Y = jailCenter.Y + offsetY;

                    _player.Angle = 0f; // 초기 각도(아래 방향)로 설정
                    _player.IsMoving = false;
                    _networkClient?.SendMoveUdp(_player);
                });
            });
        }
    }

    private async void HandleRobberDetected(Player robber)
    {
        // 중복 감지 방지 (이미 체포 중이면 스킵)
        if (_arrestVisualTimers.TryGetValue(robber.Id, out var time) && DateTime.Now < time)
            return;

        // 다른 클라이언트들에게 알림
        _networkClient?.SendArrest(_player.Id, robber.Id);

        // 로컬에서 이미지 변경 및 텍스트 표시
        TriggerArrestVisuals(_player.Id, robber.Id);

        // 화면 갱신
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _canvas.InvalidateSurface();
        });
    }
}
