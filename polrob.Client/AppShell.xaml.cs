namespace polrob.Client;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("PlayPage", typeof(PlayPage));
		Routing.RegisterRoute("GameCreate", typeof(GameCreate));
		Routing.RegisterRoute("GameJoin", typeof(GameJoin));
		Routing.RegisterRoute("GameLobby", typeof(GameLobby));
	}
}
