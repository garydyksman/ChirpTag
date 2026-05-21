namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    /// <summary>
    /// Trace combat list / presence / RX path on the debug console. Set <see cref="Enabled"/> to <c>false</c> when finished.
    /// </summary>
    public static class CombatListDiagnostics
    {
        /// <summary>Set to <c>true</c> while debugging combat list / LoRa RX; default off to limit console noise and allocations.</summary>
        public static bool Enabled = false;

        public static void Write(string message)
        {
            if (!Enabled || message == null)
            {
                return;
            }

            DebugLog.Write("[CombatList] " + message);
        }
    }
}
