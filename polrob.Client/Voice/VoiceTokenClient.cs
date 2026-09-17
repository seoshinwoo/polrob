using System.Net;
using System.Net.Http.Json;
using polrob.Shared;

namespace polrob.Client.Voice;

public sealed class VoiceTokenClient
{
    public async Task<VoiceConnectionInfo> GetConnectionInfoAsync(
        string roomId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new ArgumentException("방 ID가 필요합니다.", nameof(roomId));
        }

        await AuthSession.LoadAsync();
        if (!AuthSession.IsLoggedIn)
        {
            throw new VoiceChatException("로그인 세션이 필요합니다.");
        }

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(AuthSession.ApiBaseUrl)
        };
        AuthSession.ApplyAuthorization(httpClient);

        using var response = await httpClient.PostAsJsonAsync(
            "voice/token",
            new VoiceTokenRequest(roomId),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new VoiceChatException("로그인 세션이 만료되었습니다.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new VoiceChatException("현재 게임 방의 보이스 채널에 참가할 권한이 없습니다.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var diagnostic = await response.Content.ReadAsStringAsync(cancellationToken);
            System.Diagnostics.Debug.WriteLine(
                $"Voice token request failed ({(int)response.StatusCode}): {diagnostic}");

            var message = response.StatusCode switch
            {
                HttpStatusCode.ServiceUnavailable => "보이스 서버 설정이 완료되지 않았습니다.",
                >= HttpStatusCode.InternalServerError => "보이스 서버에 일시적인 문제가 발생했습니다.",
                HttpStatusCode.BadRequest => "보이스 채널 요청 정보가 올바르지 않습니다.",
                _ => "보이스 접속 정보를 가져오지 못했습니다."
            };
            throw new VoiceChatException(message);
        }

        return await response.Content.ReadFromJsonAsync<VoiceConnectionInfo>(
               cancellationToken: cancellationToken)
               ?? throw new VoiceChatException("보이스 접속 응답을 읽을 수 없습니다.");
    }
}
