using System.Diagnostics;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public static class DebugLog
    {
        /// <summary>Writes one line to the debug transport (no numeric prefix — avoids heap-heavy formatting).</summary>
        public static void Write(string message)
        {
            if (message == null)
            {
                return;
            }

            try
            {
                Debug.WriteLine(message);
            }
            catch
            {
                // Swallow logging failures on constrained devices.
            }
        }
    }
}
