namespace polrob.Client;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("GamePlay", typeof(GamePlay));
		Routing.RegisterRoute("GameCreate", typeof(GameCreate));
		Routing.RegisterRoute("GameJoin", typeof(GameJoin));
		Routing.RegisterRoute("GameLobby", typeof(GameLobby));
		Routing.RegisterRoute("GameOver", typeof(GameOver));
	}
}
