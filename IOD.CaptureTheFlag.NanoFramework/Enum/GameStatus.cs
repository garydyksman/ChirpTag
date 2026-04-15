namespace IOD.CaptureTheFlag.NanoFramework.Enum
{
    public enum GameStatus
    {
        None,     // no game created yet
        Waiting,  // game exists, accepting registrations
        Active,   // game running, full player list available
        Ended     // game over
    }
}
