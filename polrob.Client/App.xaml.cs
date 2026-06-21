using Microsoft.Extensions.DependencyInjection;

namespace polrob.Client;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Shell과 첫 화면을 만드는 동안 Keychain 읽기를 겹쳐서 진행합니다.
		// 이후 각 페이지의 LoadAsync 호출은 같은 작업 결과를 즉시 재사용합니다.
		_ = AuthSession.LoadAsync();
#if ANDROID
		return new Window(new AndroidStartupSplashPage());
#else
		return new Window(new AppShell());
#endif
	}
}
