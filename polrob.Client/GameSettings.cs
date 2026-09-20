using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace polrob.Client;

/// <summary>
/// 기기에 저장되는 게임 설정과 마이크 권한 진입점을 한곳에서 관리합니다.
/// </summary>
internal static class GameSettings
{
    private const string SoundVolumeKey = "settings.sound-volume";
    private const string VibrationEnabledKey = "settings.vibration-enabled";
    private const string InitialMicrophonePromptedKey = "settings.microphone-initial-prompted";
    private static readonly SemaphoreSlim InitialPermissionLock = new(1, 1);

    public static double SoundVolume
    {
        get => Math.Clamp(Preferences.Default.Get(SoundVolumeKey, 1d), 0d, 1d);
        set => Preferences.Default.Set(SoundVolumeKey, Math.Clamp(value, 0d, 1d));
    }

    public static bool VibrationEnabled
    {
        get => Preferences.Default.Get(VibrationEnabledKey, true);
        set => Preferences.Default.Set(VibrationEnabledKey, value);
    }

    public static Task<PermissionStatus> GetMicrophonePermissionStatusAsync() =>
        Permissions.CheckStatusAsync<Permissions.Microphone>();

    public static Task<PermissionStatus> RequestMicrophonePermissionAsync() =>
        Permissions.RequestAsync<Permissions.Microphone>();

    /// <summary>
    /// 설치 후 첫 메인 화면에서만 시스템 권한 창을 표시합니다. 이후 게임 화면에서는
    /// 권한을 요청하지 않으며, 사용자는 설정 화면에서 직접 다시 요청할 수 있습니다.
    /// </summary>
    public static async Task RequestMicrophonePermissionOnFirstLaunchAsync()
    {
        await InitialPermissionLock.WaitAsync();
        try
        {
            if (Preferences.Default.Get(InitialMicrophonePromptedKey, false))
            {
                return;
            }

            // 권한 창이 열린 상태에서 앱이 종료되어도 다음 실행마다 반복해서 묻지 않습니다.
            Preferences.Default.Set(InitialMicrophonePromptedKey, true);
            var status = await GetMicrophonePermissionStatusAsync();
            if (status != PermissionStatus.Granted)
            {
                await RequestMicrophonePermissionAsync();
            }
        }
        catch (Exception exception)
        {
            // 권한 API를 지원하지 않는 기기에서도 앱 시작 자체는 계속되어야 합니다.
            System.Diagnostics.Debug.WriteLine(
                $"Initial microphone permission request failed: {exception}");
        }
        finally
        {
            InitialPermissionLock.Release();
        }
    }
}
