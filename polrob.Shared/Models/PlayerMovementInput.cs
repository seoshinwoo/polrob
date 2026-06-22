using System.Text.Json.Serialization;

namespace polrob.Shared;

// 클라이언트가 서버에 보내는 이동 "의도"입니다. 좌표와 이동 결과는 포함하지 않습니다.
public sealed class PlayerMovementInput
{
    [JsonPropertyName("i")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("s")]
    public ulong Sequence { get; set; }

    [JsonPropertyName("t")]
    public string Token { get; set; } = string.Empty;
}
