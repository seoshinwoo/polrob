using System.Net.Http.Json;
using polrob.Shared;

namespace polrob.Client;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		AuthSession.Changed -= UpdateAuthHeader;
		AuthSession.Changed += UpdateAuthHeader;
		await AuthSession.LoadAsync();
		UpdateAuthHeader();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		AuthSession.Changed -= UpdateAuthHeader;
	}

	private async void OnLoginClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("Login");
	}

	private async void OnProfileClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("Profile");
	}

	private async void OnCreateClicked(object? sender, EventArgs e)
	{
		await AuthSession.LoadAsync();
		if (!AuthSession.IsLoggedIn || string.IsNullOrWhiteSpace(AuthSession.PlayerId))
		{
			await Shell.Current.GoToAsync("Login");
			return;
		}

		try
		{
			CreateButton.IsEnabled = false;
			using var httpClient = new HttpClient { BaseAddress = new Uri(AuthSession.ApiBaseUrl) };
			var response = await httpClient.PostAsJsonAsync(
				"game/create",
				new CreateRoomRequest(AuthSession.PlayerId, "custom", PlayerRole.Police, true));

			if (!response.IsSuccessStatusCode)
			{
				await DisplayAlertAsync("Create", await ReadErrorMessageAsync(response), "OK");
				return;
			}

			var serverResponse = await response.Content.ReadFromJsonAsync<ServerResponse>();
			if (serverResponse?.Success != true || string.IsNullOrWhiteSpace(serverResponse.RoomId))
			{
				await DisplayAlertAsync("Create", serverResponse?.Message ?? "방을 만들 수 없습니다.", "OK");
				return;
			}

			var roomId = Uri.EscapeDataString(serverResponse.RoomId);
			var roomCode = Uri.EscapeDataString(serverResponse.RoomCode ?? string.Empty);
			await Shell.Current.GoToAsync($"GameLobby?roomId={roomId}&roomCode={roomCode}&role={PlayerRole.Police}&isHost=true");
		}
		catch (HttpRequestException)
		{
			await DisplayAlertAsync("Create", "서버에 연결할 수 없습니다.", "OK");
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Create", $"방 생성 중 오류가 발생했습니다: {ex.Message}", "OK");
		}
		finally
		{
			CreateButton.IsEnabled = true;
		}
	}

	private async void OnJoinClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("GameJoin");
	}

	private void UpdateAuthHeader()
	{
		LoginButton.IsVisible = !AuthSession.IsLoggedIn;
		ProfileButton.IsVisible = AuthSession.IsLoggedIn;
		ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
	}

	private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
	{
		var message = await response.Content.ReadAsStringAsync();
		return string.IsNullOrWhiteSpace(message)
			? $"요청이 실패했습니다. ({(int)response.StatusCode})"
			: message.Trim('"');
	}

	private sealed record CreateRoomRequest(
		string UserId,
		string Type,
		PlayerRole Role,
		bool IsPrivate);
}
