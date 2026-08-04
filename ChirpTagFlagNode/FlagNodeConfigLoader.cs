using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using nanoFramework.Json;

namespace ChirpTagFlagNode
{
    /// <summary>
    /// Loads <see cref="FlagNodeSettings"/> from built-in JSON, then optionally merges
    /// from SPIFFS when present.
    /// </summary>
    public static class FlagNodeConfigLoader
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
        }

        private static void TryLoadBuiltIn()
        {
            try
            {
                string text = FlagNodeSettingsBuiltIn.Json;
                if (text == null || text.Length == 0)
                {
                    Debug.WriteLine("[FlagNodeConfig] built-in empty");
                    return;
                }

                if (!ApplyJsonText(text, "built-in appsettings"))
                {
                    Debug.WriteLine("[FlagNodeConfig] built-in apply failed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[FlagNodeConfig] built-in " + ex.Message);
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
                        Debug.WriteLine("[FlagNodeConfig] bad size " + p);
                        return;
                    }

                    int len = (int)lenLong;
                    byte[] buf = new byte[len];
                    int read = fs.Read(buf, 0, len);
                    if (read != len)
                    {
                        Debug.WriteLine("[FlagNodeConfig] short read " + p);
                        return;
                    }

                    string text = Encoding.UTF8.GetString(buf, 0, len);
                    if (ApplyJsonText(text, p))
                    {
                        Debug.WriteLine("[FlagNodeConfig] merged " + p);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[FlagNodeConfig] file " + ex.Message);
            }
        }

        private static bool ApplyJsonText(string text, string sourceLabel)
        {
            try
            {
                object parsed = JsonConvert.DeserializeObject(text, typeof(FlagNodeSettingsJson), JsonOptions);
                var dto = (FlagNodeSettingsJson)parsed;
                if (dto == null)
                {
                    Debug.WriteLine("[FlagNodeConfig] parse null (" + sourceLabel + ")");
                    return false;
                }

                ApplyDto(dto);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[FlagNodeConfig] parse failed (" + sourceLabel + "): " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static void ApplyDto(FlagNodeSettingsJson dto)
        {
            if (dto.FlagNodeId > 0)
            {
                FlagNodeSettings.FlagNodeId = dto.FlagNodeId;
            }

            string ssid = dto.WifiSsid == null ? string.Empty : dto.WifiSsid.Trim();
            if (ssid.Length > 0)
            {
                FlagNodeSettings.WifiSsid = ssid;
            }

            if (!string.IsNullOrEmpty(dto.WifiPassword))
            {
                FlagNodeSettings.WifiPassword = dto.WifiPassword;
            }

            string url = dto.GameApiBaseUrl == null ? string.Empty : dto.GameApiBaseUrl.Trim();
            if (url.Length > 0)
            {
                FlagNodeSettings.GameApiBaseUrl = url;
            }

            FlagNodeSettings.GameApiSslNoVerify = dto.GameApiSslNoVerify;
        }
    }
}
