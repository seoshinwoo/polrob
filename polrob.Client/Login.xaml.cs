using System.Net.Http.Json;
using Microsoft.Maui.Storage;

namespace polrob.Client;

public partial class Login : ContentPage
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri(AuthSession.ApiBaseUrl)
    };

    private bool _isSignUpMode;

    public Login()
    {
        InitializeComponent();
        SetMode(isSignUpMode: false);
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
    }

    private void OnSignInClicked(object? sender, EventArgs e)
    {
        SetMode(isSignUpMode: false);
    }

    private void OnSignUpClicked(object? sender, EventArgs e)
    {
        SetMode(isSignUpMode: true);
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        StatusLabel.Text = string.Empty;

        if (_isSignUpMode)
        {
            await SignUpAsync();
        }
        else
        {
            await LoginAsync();
        }
    }

    private void SetMode(bool isSignUpMode)
    {
        _isSignUpMode = isSignUpMode;
        DisplayNameEntry.IsVisible = isSignUpMode;
        ConfirmPasswordEntry.IsVisible = isSignUpMode;

        ContinueButton.Text = isSignUpMode ? "Sign Up" : "Login";
        StatusLabel.Text = string.Empty;

        SignInButton.Opacity = isSignUpMode ? 0.65 : 1;
        SignUpButton.Opacity = isSignUpMode ? 1 : 0.65;
    }

    private async Task SignUpAsync()
    {
        var displayName = DisplayNameEntry.Text?.Trim() ?? string.Empty;
        var loginId = LoginIdEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;
        var confirmPassword = ConfirmPasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowStatus("닉네임을 입력해주세요.", isError: true);
            return;
        }

        if (password != confirmPassword)
        {
            ShowStatus("비밀번호 확인이 일치하지 않습니다.", isError: true);
            return;
        }

        await SendAuthRequestAsync("auth/signup", new SignUpRequest(loginId, displayName, password));
    }

    private async Task LoginAsync()
    {
        var loginId = LoginIdEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;

        await SendAuthRequestAsync("auth/login", new LoginRequest(loginId, password));
    }

    private async Task SendAuthRequestAsync<TRequest>(string route, TRequest request)
    {
        try
        {
            SetBusy(true);

            var response = await _httpClient.PostAsJsonAsync(route, request);
            if (!response.IsSuccessStatusCode)
            {
                ShowStatus(await ReadErrorMessageAsync(response), isError: true);
                return;
            }

            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (loginResponse is null)
            {
                ShowStatus("서버 응답을 읽을 수 없습니다.", isError: true);
                return;
            }

            await AuthSession.SetLoggedInAsync(
                loginResponse.SessionToken,
                loginResponse.PlayerId,
                loginResponse.LoginId,
                loginResponse.DisplayName);

            ShowStatus($"{loginResponse.DisplayName}님, 환영합니다.", isError: false);
            await Shell.Current.GoToAsync("//MainPage");
        }
        catch (HttpRequestException)
        {
            ShowStatus("서버에 연결할 수 없습니다. polrob.Server가 실행 중인지 확인해주세요.", isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"로그인 처리 중 오류가 발생했습니다: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var message = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(message)
            ? $"요청이 실패했습니다. ({(int)response.StatusCode})"
            : message.Trim('"');
    }

    private void SetBusy(bool isBusy)
    {
        ContinueButton.IsEnabled = !isBusy;
        SignInButton.IsEnabled = !isBusy;
        SignUpButton.IsEnabled = !isBusy;
        ContinueButton.Text = isBusy ? "처리 중..." : (_isSignUpMode ? "Sign Up" : "Login");
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = isError ? Color.FromArgb("#ffb1b1") : Color.FromArgb("#76d859");
    }

    private sealed record SignUpRequest(string LoginId, string DisplayName, string Password);
    private sealed record LoginRequest(string LoginId, string Password);
    private sealed record LoginResponse(string SessionToken, string PlayerId, string LoginId, string DisplayName);
}
