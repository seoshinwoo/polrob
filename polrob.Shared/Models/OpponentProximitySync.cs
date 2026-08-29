using System.Text.Json.Serialization;

namespace polrob.Shared;

/// <summary>
/// 상대 위치를 공개하지 않고 근접 진동 단계만 클라이언트에 전달합니다.
/// </summary>
public sealed class OpponentProximitySync
{
    public const float MaximumSurfaceDistance = 500f;
    public const int StepMilliseconds = 100;
    public const int MaximumPulseMilliseconds = 500;

    [JsonPropertyName("p")]
    public int PulseMilliseconds { get; set; }

    public static int FromSurfaceDistance(float surfaceDistance)
    {
        if (!float.IsFinite(surfaceDistance) || surfaceDistance > MaximumSurfaceDistance)
        {
            return 0;
        }

        return Math.Clamp(
            (int)MathF.Ceiling(MathF.Max(0f, surfaceDistance) / StepMilliseconds) * StepMilliseconds,
            StepMilliseconds,
            MaximumPulseMilliseconds);
    }

    public static int NormalizePulseMilliseconds(int pulseMilliseconds)
    {
        return pulseMilliseconds >= StepMilliseconds &&
               pulseMilliseconds <= MaximumPulseMilliseconds &&
               pulseMilliseconds % StepMilliseconds == 0
            ? pulseMilliseconds
            : 0;
    }
}
