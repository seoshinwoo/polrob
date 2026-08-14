using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.Maui.Storage;

namespace polrob.Client;

public static class AuthSession
{
    // iPhone 개인용 핫스팟에 연결된 현재 MacBook(en0)의 IPv4 주소입니다.
    // iOS 실기기는 이 주소로 MacBook에서 실행 중인 HTTP/TCP/UDP 서버에 접속합니다.
    private const string LocalNetworkServerHost = "192.0.0.2";
    private static readonly SemaphoreSlim LoadLock = new(1, 1);
    private static bool _isLoaded;

    public static string? SessionToken { get; private set; }
    public static string? UserId { get; private set; }
    public static string? Name { get; private set; }

    public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(SessionToken)
        && !string.IsNullOrWhiteSpace(UserId);

    public static event Action? Changed;

    public static async Task LoadAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        await LoadLock.WaitAsync();
        try
        {
            if (_isLoaded)
            {
                return;
            }

            SessionToken = await SecureStorage.GetAsync("sessionToken");
            UserId = Preferences.Get("userId", null);
            Name = Preferences.Get("name", null);
            _isLoaded = true;
        }
        finally
        {
            LoadLock.Release();
        }

        Changed?.Invoke();
    }

    public static async Task SetLoggedInAsync(string sessionToken, string userId, string name)
    {
        SessionToken = sessionToken;
        UserId = userId;
        Name = name;
        _isLoaded = true;

        await SecureStorage.SetAsync("sessionToken", sessionToken);
        Preferences.Set("userId", userId);
        Preferences.Set("name", name);

        Changed?.Invoke();
    }

    public static async Task LogoutAsync()
    {
        var token = SessionToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                using var httpClient = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
                await httpClient.PostAsJsonAsync("auth/logout", new LogoutRequest(token));
            }
            catch
            {
                // Local logout should still complete even if the server is unavailable.
            }
        }

        ClearLocalSession();
    }

    public static void ClearLocalSession()
    {
        SessionToken = null;
        UserId = null;
        Name = null;
        _isLoaded = true;

        SecureStorage.Remove("sessionToken");
        Preferences.Remove("playerId");
        Preferences.Remove("loginId");
        Preferences.Remove("displayName");
        Preferences.Remove("userId");
        Preferences.Remove("name");

        Changed?.Invoke();
    }

    // HttpClient가 서버에 요청을 보낼 때 로그인 토큰을 Authorization 헤더에 붙여주는 함수
    public static void ApplyAuthorization(HttpClient httpClient)
    {
        httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(SessionToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", SessionToken);
    }

    public static string ApiBaseUrl
    {
        get
        {
#if ANDROID
            return $"http://{AndroidServerHost}:5174";
#elif IOS
            return $"http://{IosServerHost}:5174";
#else
            return "http://localhost:5174";
#endif
        }
    }

    public static string GameServerHost
    {
        get
        {
#if ANDROID
            return AndroidServerHost;
#elif IOS
            return IosServerHost;
#else
            return "127.0.0.1";
#endif
        }
    }

    private static string AndroidServerHost =>
        DeviceInfo.DeviceType == DeviceType.Virtual
            ? "10.0.2.2"
            : LocalNetworkServerHost;

    private static string IosServerHost =>
        DeviceInfo.DeviceType == DeviceType.Virtual
            ? "127.0.0.1"
            : LocalNetworkServerHost;

    private sealed record LogoutRequest(string SessionToken);
}
