using System.Net.Http.Json;
using Microsoft.Maui.Storage;

namespace polrob.Client;

public static class AuthSession
{
    private const string LocalNetworkServerHost = "192.0.0.2";

    public static string? SessionToken { get; private set; }
    public static string? UserId { get; private set; }
    public static string? Name { get; private set; }

    public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(SessionToken)
        && !string.IsNullOrWhiteSpace(UserId);

    public static event Action? Changed;

    public static async Task LoadAsync()
    {
        SessionToken = await SecureStorage.GetAsync("sessionToken");
        UserId = Preferences.Get("userId", null);
        Name = Preferences.Get("name", null);
        Changed?.Invoke();
    }

    public static async Task SetLoggedInAsync(string sessionToken, string userId, string name)
    {
        SessionToken = sessionToken;
        UserId = userId;
        Name = name;

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

        SessionToken = null;
        UserId = null;
        Name = null;

        SecureStorage.Remove("sessionToken");
        Preferences.Remove("playerId");
        Preferences.Remove("loginId");
        Preferences.Remove("displayName");
        Preferences.Remove("userId");
        Preferences.Remove("name");

        Changed?.Invoke();
    }

    public static string ApiBaseUrl
    {
        get
        {
#if ANDROID
            return "http://10.0.2.2:5174";
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
            return "10.0.2.2";
#elif IOS
            return IosServerHost;
#else
            return "127.0.0.1";
#endif
        }
    }

    private static string IosServerHost =>
        DeviceInfo.DeviceType == DeviceType.Virtual
            ? "127.0.0.1"
            : LocalNetworkServerHost;

    private sealed record LogoutRequest(string SessionToken);
}
