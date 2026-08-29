public sealed class LiveKitOptions
{
    public const string SectionName = "LiveKit";

    // TODO(LIVEKIT_CREDENTIALS): 로컬은 user-secrets, Azure는 App Settings/Key Vault에 설정합니다.
    public string Url { get; set; } = string.Empty;

    // TODO(LIVEKIT_CREDENTIALS): API key와 API secret은 토큰을 서명하는 서버에서만 사용합니다.
    // 특히 ApiSecret을 polrob.Client나 Git이 추적하는 appsettings.json에 넣으면 안 됩니다.
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    // 유출된 참가자 토큰이 오래 사용되지 않도록 게임 접속에 충분한 짧은 수명만 부여합니다.
    public int TokenLifetimeMinutes { get; set; } = 15;
}
