namespace polrob.Shared;

public enum TcpMessageType : byte
{
    Join = 1,
    Joined = 2,
    Left = 3,
    InitialState = 4,
    Arrested = 5,
    GameState = 6,
    JailBreak = 7,
    PlayerState = 8,
    JailBreakProgress = 9
}
