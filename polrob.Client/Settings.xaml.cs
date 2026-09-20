using Microsoft.Maui.ApplicationModel;

namespace polrob.Client;

public partial class Settings : ContentPage
{
    private bool _isLoadingSettings;
    private bool _isRequestingMicrophonePermission;

    public Settings()
    {
        InitializeComponent();
        LoadStoredSettings();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        LoadStoredSettings();
        await RefreshMicrophonePermissionAsync();
    }

    private void LoadStoredSettings()
    {
        _isLoadingSettings = true;
        SoundVolumeSlider.Value = GameSettings.SoundVolume;
        VolumeValueLabel.Text = FormatVolume(GameSettings.SoundVolume);
        VibrationSwitch.IsToggled = GameSettings.VibrationEnabled;
        _isLoadingSettings = false;
    }

    private void OnSoundVolumeChanged(object? sender, ValueChangedEventArgs e)
    {
        var volume = Math.Clamp(e.NewValue, 0d, 1d);
        VolumeValueLabel.Text = FormatVolume(volume);
        if (!_isLoadingSettings)
        {
            GameSettings.SoundVolume = volume;
        }
    }

    private void OnVibrationToggled(object? sender, ToggledEventArgs e)
    {
        if (!_isLoadingSettings)
        {
            GameSettings.VibrationEnabled = e.Value;
        }
    }

    private async void OnMicrophonePermissionClicked(object? sender, EventArgs e)
    {
        if (_isRequestingMicrophonePermission)
        {
            return;
        }

        _isRequestingMicrophonePermission = true;
        MicrophonePermissionButton.IsEnabled = false;
        try
        {
            var status = await GameSettings.GetMicrophonePermissionStatusAsync();
            if (status != PermissionStatus.Granted)
            {
                status = await GameSettings.RequestMicrophonePermissionAsync();
            }

            if (status != PermissionStatus.Granted)
            {
                var openSettings = await DisplayAlertAsync(
                    "마이크 권한",
                    "권한 요청이 거부되었습니다. 기기 설정에서 PolRob의 마이크 권한을 허용할까요?",
                    "기기 설정 열기",
                    "취소");
                if (openSettings)
                {
                    AppInfo.ShowSettingsUI();
                }
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Microphone permission request failed: {exception}");
            await DisplayAlertAsync(
                "마이크 권한",
                "마이크 권한을 요청하지 못했습니다. 기기 설정에서 권한을 확인해주세요.",
                "확인");
        }
        finally
        {
            _isRequestingMicrophonePermission = false;
            await RefreshMicrophonePermissionAsync();
        }
    }

    private async Task RefreshMicrophonePermissionAsync()
    {
        try
        {
            var status = await GameSettings.GetMicrophonePermissionStatusAsync();
            var granted = status == PermissionStatus.Granted;
            MicrophoneStatusLabel.Text = granted ? "허용됨" : GetPermissionStatusText(status);
            MicrophoneStatusLabel.TextColor = Color.FromArgb(granted ? "#72E6A5" : "#FFB56B");
            MicrophonePermissionButton.Text = granted ? "권한 허용됨" : "마이크 권한 허용";
            MicrophonePermissionButton.IsEnabled = !granted && !_isRequestingMicrophonePermission;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Microphone permission status check failed: {exception}");
            MicrophoneStatusLabel.Text = "상태를 확인할 수 없음";
            MicrophoneStatusLabel.TextColor = Color.FromArgb("#FFB1B1");
            MicrophonePermissionButton.Text = "기기 설정 열기";
            MicrophonePermissionButton.IsEnabled = !_isRequestingMicrophonePermission;
        }
    }

    private async void OnBackClicked(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..", true);
    }

    private static string FormatVolume(double volume) =>
        $"{Math.Round(Math.Clamp(volume, 0d, 1d) * 100d):0}%";

    private static string GetPermissionStatusText(PermissionStatus status) => status switch
    {
        PermissionStatus.Denied => "허용되지 않음",
        PermissionStatus.Disabled => "기기에서 비활성화됨",
        PermissionStatus.Restricted => "기기 정책으로 제한됨",
        PermissionStatus.Limited => "일부만 허용됨",
        _ => "아직 요청하지 않음"
    };
}
