using System.Text.Json.Serialization;

namespace polrob.Shared;

public sealed class PlayerMovementSync
{
    [JsonPropertyName("i")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("a")]
    public float Angle { get; set; }

    [JsonPropertyName("m")]
    public bool IsMoving { get; set; }

    public static PlayerMovementSync FromPlayer(Player player)
    {
        return new PlayerMovementSync
        {
            Id = player.Id,
            X = player.X,
            Y = player.Y,
            Angle = player.Angle,
            IsMoving = player.IsMoving
        };
    }

    public void ApplyTo(Player player)
    {
        player.X = X;
        player.Y = Y;
        player.Angle = Angle;
        player.IsMoving = IsMoving;
    }
}
