using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using nanoFramework.Json;

namespace ChirpTag
{
    /// <summary>
    /// Loads <see cref="ChirpTagSettings"/> from an embedded <c>appsettings.json</c>, then optionally merges <c>I:\appsettings.json</c> when present.
    /// Later: branch to try DI and nanoFramework 2.0 preview when ready.
    /// </summary>
    public static class ChirpTagConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>Optional overlay on device flash (SPIFFS); merges over embedded values.</summary>
        public const string DefaultConfigPath = "I:\\appsettings.json";

        private const int MaxConfigBytes = 8192;

        /// <summary>Loads embedded JSON, then merges file at <paramref name="path"/> if it exists.</summary>
        public static void TryLoad(string path = null)
        {
            TryLoadEmbedded();
            string p = path ?? DefaultConfigPath;
            if (File.Exists(p))
            {
                TryLoadFromPath(p);
            }
        }

        private static void TryLoadEmbedded()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string resName = ResolveEmbeddedAppSettingsName(asm);
                using (Stream s = resName == null ? null : asm.GetManifestResourceStream(resName))
                {
                    if (s == null)
                    {
                        Debug.WriteLine("[Config] missing embedded appsettings.json (manifest)");
                        return;
                    }

                    string text = ReadUtf8StreamUpTo(s, MaxConfigBytes);
                    if (text == null || text.Length == 0)
                    {
                        Debug.WriteLine("[Config] embedded empty");
                        return;
                    }

                    ApplyJsonText(text, "embedded appsettings.json");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Config] embedded " + ex.Message);
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

        private static string ReadUtf8StreamUpTo(Stream s, int maxBytes)
        {
            byte[] buf = new byte[maxBytes];
            int total = 0;
            while (total < maxBytes)
            {
                int n = s.Read(buf, total, maxBytes - total);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            if (total == 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buf, 0, total);
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

        private static string ResolveEmbeddedAppSettingsName(Assembly asm)
        {
            const string primary = "ChirpTag.appsettings.json";
            string[] names = asm.GetManifestResourceNames();
            if (names == null)
            {
                return null;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == primary)
                {
                    return primary;
                }
            }

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                if (n != null && n.EndsWith("appsettings.json"))
                {
                    return n;
                }
            }

            return null;
        }
    }
}
