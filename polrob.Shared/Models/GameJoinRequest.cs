namespace polrob.Shared;

// TCP 게임 입장 시 로그인 세션과 입장할 방만 전달합니다.
// 사용자 ID, 역할, 이름은 서버가 인증 세션과 로비 상태에서 결정합니다.
public sealed class GameJoinRequest
{
    public string SessionToken { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
}
