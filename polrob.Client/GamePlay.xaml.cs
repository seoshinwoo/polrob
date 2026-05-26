using System.Linq;
using Microsoft.Maui.Devices;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using polrob.Shared;
using polrob.Client.Network;

namespace polrob.Client;

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
    private const float VisionRangePlayerSizeMultiplier = 2.5f;
    private const float VisionConeAngleDegrees = 90f;
    private const byte FogOpacity = 120;

    // private SKBitmap? _playerIdleBitmap;
    // private SKBitmap?[] _playerRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _policeIdleBitmap;
    private SKBitmap?[] _policeRunBitmaps = new SKBitmap?[8];
    private SKBitmap? _robberIdleBitmap;
    private SKBitmap?[] _robberRunBitmaps = new SKBitmap?[8];

    // 부자연스러운 애니메이션과 진동을 막기 위해 좌우대칭을 맞춘 프레임 시퀀스 구성 (오른쪽이 두 번 흔들리는 문제 해결)
    private int[] _runFramePattern = { 0, 1, 2, 3, 5, 6, 7, 1 };
    private int _currentRunFrameIndex = 0;
    private float _animationTimer = 0f;

    public GamePlay()
    {
        InitializeComponent();

        _gameMap = new GameMap();

        _player = new Player
        {
            Id = Guid.NewGuid().ToString(),
            X = _gameMap.Width / 2f,
            Y = _gameMap.Height / 2f,
            Speed = 7f,
            Radius = 50f,
            Role = PlayerRole.Police,
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
            UpdatePlayerCoordsUI();
        };
        _timer.Start();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAssetsAsync();
        await InitializeNetworkAsync();
    }

    private string GetServerIpAddress()
    {
        // 실제 기기(안드로이드, 아이폰)와 시뮬레이터 모두 맥북의 핫스팟 IP(현재 192.168.0.238)를 바라보도록 통일합니다.
        return "192.168.0.238";
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
            UpdatePlayerCount();
        };

        _networkClient.OnPlayerJoined += (p) =>
        {
            if (p.Id != _player.Id) _players[p.Id] = p;
            UpdatePlayerCount();
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
            UpdatePlayerCount();
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

    private void UpdatePlayerCount()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlayerCountLabel.Text = $"Players: {_players.Count}";
            UpdatePlayerCoordsUI();
        });
    }

    private void UpdatePlayerCoordsUI()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var coords = string.Join("\n", _players.Values.Select(p =>
                $"{(p.Id == _player.Id ? "(Me) " : "")}{p.Id[..Math.Min(4, p.Id.Length)]}: {p.X:F0}, {p.Y:F0}"));
            PlayerCoordsLabel.Text = coords;
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
        _player.IsMoving = false;
        if (_activeTouchId != -1)
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
    }

    private bool IsColliding(float x, float y, float radius)
    {
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

        foreach (var player in _players.Values)
        {
            if (!ShouldDrawPlayer(player))
            {
                continue;
            }

            // 플레이어 렌더링
            SKBitmap? currentBitmap = null;
            if (player.Role == PlayerRole.Police)
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

                if (!player.IsMoving || currentBitmap == _policeIdleBitmap || currentBitmap == _robberIdleBitmap)
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

        canvas.Restore();

        // 3. UI 오버레이 렌더링 코드는 카메라 복구 후에 그림
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
}
