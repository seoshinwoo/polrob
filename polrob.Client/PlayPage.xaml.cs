using Microsoft.Maui.Devices;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace polrob.Client;

public partial class PlayPage : ContentPage
{
    private float _playerX = -1;
    private float _playerY = -1;
    private float _playerSpeed = 7f;
    private float _playerRadius = 50f;

    // Shared Map
    private Shared.Map _gameMap;

    // Joystick state
    private SKPoint _joystickCenter;
    private SKPoint _joystickThumb;
    private float _joystickRadius = 150f;
    private float _thumbRadius = 50f;
    private long _activeTouchId = -1;

    private readonly IDispatcherTimer _timer;
    private SKCanvasView _canvas;

    public PlayPage()
    {
        InitializeComponent();

        _gameMap = new Shared.Map();
        _playerX = _gameMap.Width / 2f;
        _playerY = _gameMap.Height / 2f;

        _canvas = new SKCanvasView();
        _canvas.EnableTouchEvents = true;
        _canvas.Touch += Canvas_Touch;
        _canvas.PaintSurface += Canvas_PaintSurface;
        Container.Children.Add(_canvas);

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
        _timer.Tick += (s, e) =>
        {
            UpdatePhysics();
            _canvas.InvalidateSurface();
        };
        _timer.Start();
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
        if (_activeTouchId != -1)
        {
            var dx = _joystickThumb.X - _joystickCenter.X;
            var dy = _joystickThumb.Y - _joystickCenter.Y;

            // 이동 방향 계산
            if (_joystickRadius > 0)
            {
                var moveX = dx / _joystickRadius * _playerSpeed;
                var moveY = dy / _joystickRadius * _playerSpeed;

                var newX = _playerX + moveX;
                var newY = _playerY + moveY;

                // 맵 경계 충돌 처리
                if (newX - _playerRadius < 0) newX = _playerRadius;
                if (newX + _playerRadius > _gameMap.Width) newX = _gameMap.Width - _playerRadius;
                if (newY - _playerRadius < 0) newY = _playerRadius;
                if (newY + _playerRadius > _gameMap.Height) newY = _gameMap.Height - _playerRadius;

                // 벽을 따라 미끄러지도록 X축, Y축 각각 충돌 검사
                if (!IsColliding(newX, _playerY))
                {
                    _playerX = newX;
                }

                if (!IsColliding(_playerX, newY))
                {
                    _playerY = newY;
                }
            }
        }
    }

    private bool IsColliding(float x, float y)
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

                if ((distanceX * distanceX) + (distanceY * distanceY) < (_playerRadius * _playerRadius))
                {
                    return true;
                }
            }
            else if (obs.Type == "Circle")
            {
                float dx = x - obs.CenterX.X;
                float dy = y - obs.CenterX.Y;
                float radiusSum = _playerRadius + obs.Radius;

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
        canvas.Clear(SKColors.DarkSlateGray);

        canvas.Save();

        // 1. 카메라 설정 (World Space로 이동)
        // 화면의 중심이 플레이어를 따라다니게 캔버스를 이동시킴
        canvas.Translate(width / 2f - _playerX, height / 2f - _playerY);

        // 2. 월드 렌더링
        using (var mapPaint = new SKPaint { Color = SKColors.LightGray, Style = SKPaintStyle.Stroke, StrokeWidth = 10 })
        {
            canvas.DrawRect(0, 0, _gameMap.Width, _gameMap.Height, mapPaint);
        }

        // 장애물 렌더링
        using (var obsPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill })
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

        // 플레이어 렌더링
        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(_playerX, _playerY, _playerRadius, paint);

        // 멀티플레이어라면 여기서 상대방들의 X, Y 좌표값을 통해 추가로 DrawCircle 등을 해주면 됩니다.

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
}