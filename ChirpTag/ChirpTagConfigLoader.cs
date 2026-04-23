using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using nanoFramework.Json;

namespace ChirpTag
{
    /// <summary>
    /// Loads <see cref="ChirpTagSettings"/> from built-in JSON (<see cref="AppSettingsBuiltIn"/>), then optionally merges <c>I:\appsettings.json</c> when present.
    /// Later: branch to try DI and nanoFramework 2.0 preview when ready.
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

                ApplyJsonText(text, "built-in appsettings");
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
                    ApplyJsonText(text, p);
                }

                Debug.WriteLine("[Config] merged " + p);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Config] file " + ex.Message);
            }
        }

        private static void ApplyJsonText(string text, string sourceLabel)
        {
            object parsed = JsonConvert.DeserializeObject(text, typeof(AppSettingsJson), JsonOptions);
            var dto = (AppSettingsJson)parsed;
            if (dto == null)
            {
                Debug.WriteLine("[Config] parse null (" + sourceLabel + ")");
                return;
            }

            ApplyDto(dto);
        }

        private static void ApplyDto(AppSettingsJson dto)
        {
            if (dto.PlayerName != null)
            {
                ChirpTagSettings.PlayerName = dto.PlayerName;
            }

            if (dto.WifiSsid != null)
            {
                ChirpTagSettings.WifiSsid = dto.WifiSsid;
            }

            if (dto.WifiPassword != null)
            {
                ChirpTagSettings.WifiPassword = dto.WifiPassword;
            }

            if (dto.GameApiBaseUrl != null)
            {
                ChirpTagSettings.GameApiBaseUrl = dto.GameApiBaseUrl;
            }

            ChirpTagSettings.GameApiSslNoVerify = dto.GameApiSslNoVerify;
            ChirpTagSettings.AllowHudWithoutValidDeviceId = dto.AllowHudWithoutValidDeviceId;
        }
    }
}
