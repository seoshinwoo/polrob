namespace polrob.Client.Voice;

public sealed class VoiceChatException : Exception
{
    public VoiceChatException(string message)
        : base(message)
    {
    }

    public VoiceChatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
