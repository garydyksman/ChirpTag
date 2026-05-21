using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using IOD.CaptureTheFlag.NanoFramework.Implementations;
using nanoFramework.Json;

namespace ChirpTag
{
    /// <summary>
    /// Loads <see cref="ChirpTagSettings"/> from built-in JSON (<see cref="AppSettingsBuiltIn"/>), then optionally merges
    /// <see cref="DefaultConfigPath"/> when present. Does not auto-merge a CWD <c>appsettings.json</c>: a partial or empty
    /// file next to the PE would deserialize with default bools and empty strings and wipe built-in settings.
    /// </summary>
    public static class ChirpTagConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>Optional overlay on device flash (SPIFFS); merges over built-in values.</summary>
        public const string DefaultConfigPath = "I:\\appsettings.json";

        private const int MaxConfigBytes = 8192;

        /// <summary>Loads built-in defaults, then merges file at <paramref name="path"/> if it exists.</summary>
        public static void TryLoad(string path = null)
        {
            TryLoadBuiltIn();
            string p = path ?? DefaultConfigPath;
            if (File.Exists(p))
            {
                TryLoadFromPath(p);
            }

            string url = ChirpTagSettings.GameApiBaseUrl;
            int urlLen = url == null ? 0 : url.Length;
            string pn = ChirpTagSettings.PlayerName;
            int pnLen = pn == null ? 0 : pn.Length;
            string ssid = ChirpTagSettings.WifiSsid;
            int ssidLen = ssid == null ? 0 : ssid.Length;
            //DebugLog.Write(
            //    "[Config] TryLoad merged playerNameLen=" + pnLen.ToString() + " urlLen=" + urlLen.ToString() + " wifiSsidLen=" + ssidLen.ToString() + " sslNoVerify=" + (ChirpTagSettings.GameApiSslNoVerify ? "1" : "0") + " ignoreAttackRangeLimit=" + (ChirpTagSettings.IgnoreAttackRangeLimit ? "1" : "0"));
        }

        private static void TryLoadBuiltIn()
        {
            try
            {
                string text = AppSettingsBuiltIn.Json;
                if (text == null || text.Length == 0)
                {
                    Debug.WriteLine("[Config] built-in empty");
                    return;
                }

                if (!ApplyJsonText(text, "built-in appsettings"))
                {
                    Debug.WriteLine("[Config] built-in apply failed (see parse failed line)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Config] built-in " + ex.Message);
            }
        }

        private static void TryLoadFromPath(string p)
        {
            try
            {
                using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                {
                    long lenLong = fs.Length;
                    if (lenLong <= 0 || lenLong > MaxConfigBytes)
                    {
                        Debug.WriteLine("[Config] bad size " + p);
                        return;
                    }

                    int len = (int)lenLong;
                    byte[] buf = new byte[len];
                    int read = fs.Read(buf, 0, len);
                    if (read != len)
                    {
                        Debug.WriteLine("[Config] short read " + p);
                        return;
                    }

                    string text = Encoding.UTF8.GetString(buf, 0, len);
                    if (ApplyJsonText(text, p))
                    {
                        Debug.WriteLine("[Config] merged " + p);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Config] file " + ex.Message);
            }
        }

        /// <returns><c>false</c> if JSON cannot be deserialized (wrong types, corrupt file); built-in values are unchanged for that overlay.</returns>
        private static bool ApplyJsonText(string text, string sourceLabel)
        {
            try
            {
                object parsed = JsonConvert.DeserializeObject(text, typeof(AppSettingsJson), JsonOptions);
                var dto = (AppSettingsJson)parsed;
                if (dto == null)
                {
                    Debug.WriteLine("[Config] parse null (" + sourceLabel + ")");
                    return false;
                }

                ApplyDto(dto);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Config] parse failed (" + sourceLabel + "): " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static void ApplyDto(AppSettingsJson dto)
        {
            // Only overwrite when the overlay supplies a real value. JSON "" or a partial file must not
            // erase built-in strings; omitted bools deserialize as false and must not wipe prior true.
            string pn = dto.PlayerName == null ? string.Empty : dto.PlayerName.Trim();
            if (pn.Length > 0)
            {
                ChirpTagSettings.PlayerName = pn;
            }

            string ssid = dto.WifiSsid == null ? string.Empty : dto.WifiSsid.Trim();
            if (ssid.Length > 0)
            {
                ChirpTagSettings.WifiSsid = ssid;
            }

            if (!string.IsNullOrEmpty(dto.WifiPassword))
            {
                ChirpTagSettings.WifiPassword = dto.WifiPassword;
            }

            string url = dto.GameApiBaseUrl == null ? string.Empty : dto.GameApiBaseUrl.Trim();
            if (url.Length > 0)
            {
                ChirpTagSettings.GameApiBaseUrl = url;
            }

            ChirpTagSettings.GameApiSslNoVerify = dto.GameApiSslNoVerify;
            ChirpTagSettings.AllowHudWithoutValidDeviceId = dto.AllowHudWithoutValidDeviceId;
            ChirpTagSettings.IgnoreAttackRangeLimit = dto.IgnoreAttackRangeLimit;
        }
    }
}
