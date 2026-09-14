using System;
using System.Net.NetworkInformation;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    /// <summary>
    /// WiFi utility methods for both game device and flag node.
    /// </summary>
    public static class WiFiHelper
    {

        /// <summary>
        /// Gets the WiFi MAC address as a colon-separated hex string (e.g. "AA:BB:CC:DD:EE:FF").
        /// Returns empty string if no WiFi interface found.
        /// </summary>
        public static string GetMacAddress()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                if (interfaces == null || interfaces.Length == 0)
                {
                    return string.Empty;
                }

                byte[] mac = interfaces[0].PhysicalAddress;
                if (mac == null || mac.Length == 0)
                {
                    return string.Empty;
                }

                string result = string.Empty;
                for (int i = 0; i < mac.Length; i++)
                {
                    if (i > 0)
                    {
                        result = result + ":";
                    }

                    result = result + ByteToHex(mac[i]);
                }

                return result;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ByteToHex(byte b)
        {
            const string hex = "0123456789ABCDEF";
            return new string(new char[] { hex[b >> 4], hex[b & 0x0F] });
        }
    }
}
