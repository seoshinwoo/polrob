namespace polrob.Shared;

public class JailBreakProgressSync
{
    public string RoomId { get; set; } = string.Empty;
    public Dictionary<string, float> ProgressByRescuer { get; set; } = new();
}
