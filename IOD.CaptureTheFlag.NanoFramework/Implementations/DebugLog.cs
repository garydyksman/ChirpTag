using System.Diagnostics;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public static class DebugLog
    {
        private static int _sequence;
        private const string DisplayPrefix = "[Display]";

        public static void Write(string message)
        {
            try
            {
                if (message == null || !message.StartsWith(DisplayPrefix))
                    return;

                _sequence++;
                Debug.WriteLine("[" + _sequence + "] " + message);
            }
            catch
            {
                // Swallow logging failures on constrained devices.
            }
        }
    }
}
