using System;
using Microsoft.Identity.Client;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace polrob.Client;

public partial class Login : ContentPage
{
    // TODO: replace with your actual client id from App Registration
    private const string ClientId = "msalb3505f2d-66d8-48c2-a59f-953bcc54ae2b://auth";
    // TODO: replace with your server auth endpoint
    private const string ServerAuthUrl = "https://localhost:5001/auth/login";

    public Login()
    {
        InitializeComponent();
    }

    private async void OnSignInClicked(object sender, EventArgs e)
    {
        SignInButton.IsEnabled = false;
        StatusLabel.Text = "Signing in...";

        try
        {
            var app = PublicClientApplicationBuilder
                .Create(ClientId)
                .WithRedirectUri($"msal{ClientId}://auth")
                .Build();

            string[] scopes = new[] { "openid", "profile" };

            var result = await app.AcquireTokenInteractive(scopes).ExecuteAsync();

            string token = result.AccessToken ?? result.IdToken ?? string.Empty;
            if (string.IsNullOrEmpty(token))
            {
                StatusLabel.Text = "No token received.";
                return;
            }

            StatusLabel.Text = "Sending token to server...";

            using var http = new HttpClient();
            var payload = new { token };
            var res = await http.PostAsJsonAsync(ServerAuthUrl, payload);

            if (!res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync();
                StatusLabel.Text = $"Server rejected token: {res.StatusCode} - {text}";
                return;
            }

            var loginResp = await res.Content.ReadFromJsonAsync<LoginResponse>();
            if (loginResp == null || string.IsNullOrEmpty(loginResp.SessionToken))
            {
                StatusLabel.Text = "Invalid server response.";
                return;
            }

            // Store session token (example: Preferences) and navigate
            Preferences.Set("sessionToken", loginResp.SessionToken);
            Preferences.Set("playerId", loginResp.PlayerId ?? string.Empty);

            StatusLabel.Text = "Login successful.";

            // Navigate to main page or game lobby
            await Shell.Current.GoToAsync("//MainPage");
        }
        catch (MsalException mex)
        {
            StatusLabel.Text = $"MSAL error: {mex.Message}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private class LoginResponse
    {
        [JsonPropertyName("sessionToken")]
        public string? SessionToken { get; set; }

        [JsonPropertyName("playerId")]
        public string? PlayerId { get; set; }
    }
}
