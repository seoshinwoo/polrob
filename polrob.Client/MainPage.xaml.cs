namespace polrob.Client;

public partial class MainPage : ContentPage
{
	int count = 0;

	public MainPage()
	{
		InitializeComponent();
	}

	private async void OnCreateClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("GameCreate");
	}

	private async void OnJoinClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("GameJoin");
	}
}
