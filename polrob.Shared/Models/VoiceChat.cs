namespace polrob.Shared;

// 클라이언트는 roomId만 보냅니다. 역할은 조작 방지를 위해 서버의 GameRoomService에서 조회합니다.
public sealed record VoiceTokenRequest(string RoomId);

// API key/secret은 포함하지 않습니다. 짧게 유효한 참가자 토큰만 클라이언트에 전달합니다.
public sealed record VoiceConnectionInfo(
    string ServerUrl,
    string ParticipantToken,
    string RoomName,
    PlayerRole Role,
    DateTime ExpiresAtUtc);
