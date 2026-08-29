using Microsoft.Maui.Devices;

#if IOS
using CoreHaptics;
#endif

namespace polrob.Client;

/// <summary>
/// 지정된 길이의 근접 경고 진동을 플랫폼에서 재생합니다.
/// iOS의 기본 MAUI 진동은 항상 500ms이므로 Core Haptics를 사용합니다.
/// </summary>
internal sealed class ProximityHaptics : IDisposable
{
#if IOS
    private CHHapticEngine? _engine;
    private ICHHapticPatternPlayer? _player;
#endif

    public void PlayPulse(TimeSpan duration)
    {
        try
        {
#if IOS
            PlayIosPulse(duration);
#else
            Vibration.Default.Vibrate(duration);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Proximity haptic error: {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
#if IOS
            if (_player != null)
            {
                _player.Stop(0d, out _);
                _player = null;
            }
#else
            Vibration.Default.Cancel();
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Proximity haptic stop error: {ex.Message}");
        }
    }

#if IOS
    private void PlayIosPulse(TimeSpan duration)
    {
        if (!CHHapticEngine.GetHardwareCapabilities().SupportsHaptics)
        {
            return;
        }

        Stop();
        _engine ??= CreateIosEngine();
        if (_engine == null)
        {
            return;
        }

        if (!_engine.Start(out var startError))
        {
            if (startError != null)
            {
                System.Diagnostics.Debug.WriteLine($"Core Haptics start error: {startError}");
            }
            return;
        }

        var eventParameters = new[]
        {
            new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, 1f),
            new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.45f)
        };
        var hapticEvent = new CHHapticEvent(
            CHHapticEventType.HapticContinuous,
            eventParameters,
            0d,
            duration.TotalSeconds);
        var pattern = new CHHapticPattern(
            new[] { hapticEvent },
            Array.Empty<CHHapticDynamicParameter>(),
            out var patternError);

        if (patternError != null)
        {
            System.Diagnostics.Debug.WriteLine($"Core Haptics pattern error: {patternError}");
            return;
        }

        _player = _engine.CreatePlayer(pattern, out var playerError);
        if (_player == null || playerError != null)
        {
            if (playerError != null)
            {
                System.Diagnostics.Debug.WriteLine($"Core Haptics player error: {playerError}");
            }
            return;
        }

        _player.Start(0d, out var playError);
        if (playError != null)
        {
            System.Diagnostics.Debug.WriteLine($"Core Haptics playback error: {playError}");
        }
    }

    private static CHHapticEngine? CreateIosEngine()
    {
        var engine = new CHHapticEngine(out var error)
        {
            AutoShutdownEnabled = true,
            PlaysHapticsOnly = true
        };

        if (error == null)
        {
            return engine;
        }

        System.Diagnostics.Debug.WriteLine($"Core Haptics initialization error: {error}");
        engine.Dispose();
        return null;
    }
#endif

    public void Dispose()
    {
        Stop();
#if IOS
        _engine?.Stop(null);
        _engine?.Dispose();
        _engine = null;
#endif
    }
}
