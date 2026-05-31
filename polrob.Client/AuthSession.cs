using System.Net.Http.Json;
using Microsoft.Maui.Storage;

namespace polrob.Client;

public static class AuthSession
{
    public static string? SessionToken { get; private set; }
    public static string? PlayerId { get; private set; }
    public static string? LoginId { get; private set; }
    public static string? DisplayName { get; private set; }

    public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(SessionToken)
        && !string.IsNullOrWhiteSpace(PlayerId);

    public static event Action? Changed;

    public static async Task LoadAsync()
    {
        SessionToken = await SecureStorage.GetAsync("sessionToken");
        PlayerId = Preferences.Get("playerId", null);
        LoginId = Preferences.Get("loginId", null);
        DisplayName = Preferences.Get("displayName", null);
        Changed?.Invoke();
    }

    public static async Task SetLoggedInAsync(string sessionToken, string playerId, string loginId, string displayName)
    {
        SessionToken = sessionToken;
        PlayerId = playerId;
        LoginId = loginId;
        DisplayName = displayName;

        await SecureStorage.SetAsync("sessionToken", sessionToken);
        Preferences.Set("playerId", playerId);
        Preferences.Set("loginId", loginId);
        Preferences.Set("displayName", displayName);

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
        PlayerId = null;
        LoginId = null;
        DisplayName = null;

        SecureStorage.Remove("sessionToken");
        Preferences.Remove("playerId");
        Preferences.Remove("loginId");
        Preferences.Remove("displayName");

        Changed?.Invoke();
    }

    public static string ApiBaseUrl
    {
        get
        {
#if ANDROID
            return "http://10.0.2.2:5174";
#else
            return "http://localhost:5174";
#endif
        }
    }

    private sealed record LogoutRequest(string SessionToken);
}
