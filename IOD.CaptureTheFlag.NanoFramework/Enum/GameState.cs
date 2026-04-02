namespace IOD.CaptureTheFlag.NanoFramework.Enum
{
    public enum GameState
    {
        Idle,        // waiting for game start
        Active,      // in game, can attack and capture
        Stunned,     // lost combat, must return to base
        Capturing,   // holding at enemy flag node (dwell timer running)
        Delivering   // holding at own flag node with enemy key
    }
}
