using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Threading;
using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using nanoFramework.Json;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    /// <summary>
    /// Calls the Capture The Flag HTTP API with <see cref="HttpWebRequest"/> (not <see cref="System.Net.Http.HttpClient"/>).
    /// On ESP32 nanoFramework, <c>HttpClient</c> can throw while reading HTTPS bodies; <c>HttpWebRequest</c> matches the stable smoke-test path.
    /// JSON: UTF-8, camelCase, numeric <c>status</c> 0–3, <c>players</c> as an array.
    /// </summary>
    public sealed class GameApiHttpClient : IGameHttpClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private const int HttpTimeoutMs = 60_000;
        private const int MaxResponseBytes = 24 * 1024;

        /// <summary>POST retries: transient failures. Do not close <see cref="HttpWebRequest.GetRequestStream"/> before <see cref="HttpWebRequest.GetResponse"/> — NF disposes the TLS session early and breaks response parse.</summary>
        private const int PostJsonMaxAttempts = 8;

        private const int PostJsonRetryBaseDelayMs = 120;
        private const int PostStreamSettleMs = 100;

        private readonly string _root;
        private readonly SslVerification _sslVerification;

        /// <param name="sslVerification">
        /// Use <see cref="SslVerification.CertificateRequired"/> in production when the device has a suitable CA bundle.
        /// Dev hosts (for example dev tunnels) often fail verification on ESP32 because the native TLS store is minimal.
        /// </param>
        public GameApiHttpClient(string baseUrl, SslVerification sslVerification = SslVerification.CertificateRequired)
        {
            if (baseUrl == null || baseUrl.Length == 0)
            {
                throw new ArgumentException("baseUrl is required.", nameof(baseUrl));
            }

            _root = baseUrl.TrimEnd('/');
            _sslVerification = sslVerification;
        }

        private void ConfigureRequest(HttpWebRequest req)
        {
            req.SslProtocols = SslProtocols.Tls12;
            req.SslVerification = _sslVerification;
            req.Timeout = HttpTimeoutMs;
            req.ReadWriteTimeout = HttpTimeoutMs;
            req.KeepAlive = false;
            req.UserAgent = "ChirpTag/1";
        }

        public GameInfo GetCurrentGame()
        {
            try
            {
                string json = GetJson(_root + "/api/game", "/api/game");
                var dto = (GameInfoDto)JsonConvert.DeserializeObject(json, typeof(GameInfoDto), JsonOptions);
                if (dto == null)
                {
                    return EmptyGame();
                }

                var status = (GameStatus)ClampEnum(dto.Status, 0, 3);
                PlayerInfo[] players;
                if (dto.Players == null || dto.Players.Length == 0)
                {
                    players = new PlayerInfo[0];
                }
                else
                {
                    players = new PlayerInfo[dto.Players.Length];
                    for (int i = 0; i < dto.Players.Length; i++)
                    {
                        PlayerInfoDto p = dto.Players[i];
                        players[i] = new PlayerInfo
                        {
                            DeviceId = (byte)ClampByte(p != null ? p.DeviceId : 0),
                            Name = p != null && p.Name != null ? p.Name : string.Empty,
                            Team = p != null && p.Team != null ? p.Team : string.Empty,
                            CombatScore = (byte)ClampByte(p != null ? p.CombatScore : 0),
                            EnemyFlagId = (byte)ClampByte(p != null ? p.EnemyFlagId : 0),
                        };
                    }
                }

                return new GameInfo { Status = status, Players = players };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[GameApi] GetCurrentGame: " + ex);
                return EmptyGame();
            }
        }

        /// <inheritdoc cref="IGameHttpClient.Register"/>
        public PlayerSetup Register(string playerName)
        {
            try
            {
                var body = new Hashtable();
                body.Add("playerName", NormalizePlayerName(playerName));
                string jsonBody = JsonSerializer.SerializeObject(body);
                string json = PostJson("/api/game/register", jsonBody);
                var dto = (PlayerSetupDto)JsonConvert.DeserializeObject(json, typeof(PlayerSetupDto), JsonOptions);
                if (dto == null)
                {
                    return new PlayerSetup { DeviceId = 0 };
                }

                return new PlayerSetup { DeviceId = (byte)ClampByte(dto.DeviceId) };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[GameApi] Register summary: " + ex.Message);
                Debug.WriteLine("[GameApi] Register chain: " + FormatExceptionChainForLog(ex, 8));
                return new PlayerSetup { DeviceId = 0 };
            }
        }

        public void ReportCombat(byte winnerId, byte loserId, bool loserHadKey)
        {
            try
            {
                var body = new Hashtable();
                body.Add("winnerId", (int)winnerId);
                body.Add("loserId", (int)loserId);
                body.Add("loserHadKey", loserHadKey);
                string jsonBody = JsonSerializer.SerializeObject(body);
                PostJson("/api/game/combat", jsonBody);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[GameApi] ReportCombat: " + ex);
            }
        }

        public bool ReportDeliver(byte deviceId, byte[] key)
        {
            try
            {
                var body = new Hashtable();
                body.Add("deviceId", (int)deviceId);
                body.Add("key", key == null || key.Length == 0 ? null : Convert.ToBase64String(key));
                string jsonBody = JsonSerializer.SerializeObject(body);
                string json = PostJson("/api/game/deliver", jsonBody);
                var dto = (DeliverAcceptedDto)JsonConvert.DeserializeObject(json, typeof(DeliverAcceptedDto), JsonOptions);
                return dto != null && dto.Accepted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[GameApi] ReportDeliver: " + ex);
                return false;
            }
        }

        public byte GetRespawnNumber(byte deviceId)
        {
            try
            {
                string json = GetJson(_root + "/api/game/respawn/" + deviceId.ToString(), "/api/game/respawn");
                var dto = (RespawnScoreDto)JsonConvert.DeserializeObject(json, typeof(RespawnScoreDto), JsonOptions);
                if (dto == null)
                {
                    return (byte)((deviceId % 5) + 1);
                }

                return (byte)ClampByte(dto.CombatScore);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[GameApi] GetRespawnNumber: " + ex);
                return (byte)((deviceId % 5) + 1);
            }
        }

        private string GetJson(string requestUri, string pathForErrors)
        {
            var req = (HttpWebRequest)WebRequest.Create(requestUri);
            req.Method = "GET";
            ConfigureRequest(req);

            using (WebResponse wr = SafeGetResponse(req, pathForErrors, null))
            {
                var resp = (HttpWebResponse)wr;
                int code = (int)resp.StatusCode;
                if (code < 200 || code > 299)
                {
                    throw new InvalidOperationException("HTTP " + code.ToString() + " " + pathForErrors);
                }

                using (Stream body = resp.GetResponseStream())
                {
                    return ReadResponseBody(body);
                }
            }
        }

        private string PostJson(string relativePath, string jsonBody)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= PostJsonMaxAttempts; attempt++)
            {
                try
                {
                    return PostJsonOnce(relativePath, jsonBody);
                }
                catch (Exception ex)
                {
                    last = ex;
                    Debug.WriteLine("[GameApi] PostJson retry " + attempt.ToString() + "/" + PostJsonMaxAttempts.ToString() + " summary=" + ex.Message);
                    Debug.WriteLine("[GameApi] PostJson retry chain: " + FormatExceptionChainForLog(ex, 8));
                    if (ExceptionIndicatesHttpConflict(ex))
                    {
                        throw last;
                    }

                    if (attempt < PostJsonMaxAttempts)
                    {
                        Thread.Sleep(PostJsonRetryBaseDelayMs + (attempt * 60));
                    }
                }
            }

            throw last;
        }

        /// <summary>Leading/trailing ASCII control/space trimmed so the device sends the same string the server stores (case-sensitive match).</summary>
        public static string NormalizePlayerName(string s)
        {
            if (s == null || s.Length == 0)
            {
                return string.Empty;
            }

            int start = 0;
            int end = s.Length - 1;
            while (start <= end && s[start] <= ' ')
            {
                start++;
            }

            while (end >= start && s[end] <= ' ')
            {
                end--;
            }

            if (end < start)
            {
                return string.Empty;
            }

            return s.Substring(start, end - start + 1);
        }

        /// <summary>409 Conflict is permanent; retrying will not help (e.g. duplicate name in lobby, new player while game active).</summary>
        private static bool ExceptionIndicatesHttpConflict(Exception ex)
        {
            const string tokenHttp = "HTTP 409";
            const string tokenAttr = "http=409";
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                string m = e.Message;
                if (m != null && (ContainsSubstring(m, tokenHttp) || ContainsSubstring(m, tokenAttr)))
                {
                    return true;
                }

                if (e is WebException wex)
                {
                    try
                    {
                        if (wex.Response is HttpWebResponse http && (int)http.StatusCode == 409)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static bool ContainsSubstring(string haystack, string needle)
        {
            if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length)
            {
                return false;
            }

            int last = haystack.Length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                int j = 0;
                for (; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        break;
                    }
                }

                if (j == needle.Length)
                {
                    return true;
                }
            }

            return false;
        }

        private string PostJsonOnce(string relativePath, string jsonBody)
        {
            string url = _root + relativePath;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            ConfigureRequest(req);
            req.ProtocolVersion = HttpVersion.Version10;
            req.SendChunked = false;

            byte[] payload = Encoding.UTF8.GetBytes(jsonBody);
            req.ContentLength = payload.Length;

            Stream ws = null;
            try
            {
                ws = req.GetRequestStream();
                ws.Write(payload, 0, payload.Length);
                try
                {
                    ws.Flush();
                }
                catch
                {
                }

                Thread.Sleep(PostStreamSettleMs);

                using (WebResponse wr = SafeGetResponse(req, relativePath, jsonBody))
                {
                    var resp = (HttpWebResponse)wr;
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code > 299)
                    {
                        throw new InvalidOperationException("HTTP " + code.ToString() + " " + relativePath);
                    }

                    using (Stream body = resp.GetResponseStream())
                    {
                        return ReadResponseBody(body);
                    }
                }
            }
            finally
            {
                if (ws != null)
                {
                    try
                    {
                        ws.Close();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// <see cref="HttpWebRequest.GetResponse"/> throws <see cref="WebException"/> on failures; message is often only "GetResponse() failed".
        /// Surface <see cref="WebExceptionStatus"/>, optional HTTP status, error body, and request preview.
        /// </summary>
        private static WebResponse SafeGetResponse(HttpWebRequest req, string pathForLogs, string requestBodyPreview)
        {
            try
            {
                return req.GetResponse();
            }
            catch (WebException ex)
            {
                throw new InvalidOperationException(FormatWebFailure(ex, pathForLogs, requestBodyPreview), ex);
            }
        }

        /// <summary>Walks <see cref="Exception.InnerException"/> for readable one-line diagnostics (nanoFramework stacks are often vague at the root).</summary>
        private static string FormatExceptionChainForLog(Exception ex, int maxDepth)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            string acc = string.Empty;
            Exception cur = ex;
            for (int d = 0; d < maxDepth && cur != null; d++)
            {
                string typeName = cur.GetType().Name;
                string msg = cur.Message != null ? cur.Message : string.Empty;
                msg = TruncateForLog(msg, 420);
                acc = acc + "[" + d.ToString() + ":" + typeName + " msg=" + msg;
                if (cur.StackTrace != null && cur.StackTrace.Length > 0)
                {
                    acc = acc + " at=" + TruncateForLog(FlattenToSingleLine(cur.StackTrace), 220);
                }

                acc = acc + "] ";
                cur = cur.InnerException;
            }

            return acc.TrimEnd();
        }

        private static string FormatWebFailure(WebException ex, string path, string requestBodyPreview)
        {
            int statusInt = (int)ex.Status;
            string statusText = ex.Status.ToString();
            string baseMsg = ex.Message != null ? ex.Message : string.Empty;
            string part = "path=" + path + " WebExceptionStatus=" + statusText + "(" + statusInt.ToString() + ") msg=" + baseMsg;
            if (statusInt == 0)
            {
                part = part + " [hint:0=Success in .NET enum; NF often leaves this unset when wrapping transport errors—see inner chain]";
            }

            part = part + " innerChain=" + (ex.InnerException == null ? "(none)" : FormatExceptionChainForLog(ex.InnerException, 5));

            if (ex.Response != null)
            {
                try
                {
                    using (WebResponse err = ex.Response)
                    {
                        var http = err as HttpWebResponse;
                        int code = http != null ? (int)http.StatusCode : 0;
                        using (Stream s = err.GetResponseStream())
                        {
                            string bodyPreview = ReadResponseBodyCapped(s, 512);
                            part = part + " http=" + code.ToString() + " body=" + bodyPreview;
                        }
                    }
                }
                catch
                {
                    part = part + " (could not read error response body)";
                }
            }

            if (requestBodyPreview != null && requestBodyPreview.Length > 0)
            {
                part = part + " requestJson=" + TruncateForLog(requestBodyPreview, 220);
            }

            return part;
        }

        /// <summary>nanoFramework <see cref="string"/> has no <c>Replace</c>; flatten stack traces for one-line logs.</summary>
        private static string FlattenToSingleLine(string s)
        {
            if (s == null || s.Length == 0)
            {
                return string.Empty;
            }

            var buf = new char[s.Length];
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r' || c == '\n' || c == '\t')
                {
                    if (n > 0 && buf[n - 1] != ' ')
                    {
                        buf[n++] = ' ';
                    }
                }
                else
                {
                    buf[n++] = c;
                }
            }

            return new string(buf, 0, n);
        }

        private static string TruncateForLog(string s, int maxChars)
        {
            if (s == null || s.Length == 0)
            {
                return string.Empty;
            }

            if (s.Length <= maxChars)
            {
                return s;
            }

            return s.Substring(0, maxChars) + "...";
        }

        private static string ReadResponseBodyCapped(Stream stream, int maxBytes)
        {
            if (stream == null)
            {
                return string.Empty;
            }

            var buffer = new byte[maxBytes];
            int total = 0;
            while (total < maxBytes)
            {
                int n = stream.Read(buffer, total, maxBytes - total);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            return Encoding.UTF8.GetString(buffer, 0, total);
        }

        private static string ReadResponseBody(Stream stream)
        {
            var buffer = new byte[512];
            int total = 0;
            while (true)
            {
                if (total == buffer.Length)
                {
                    int newLen = buffer.Length * 2;
                    if (newLen > MaxResponseBytes)
                    {
                        throw new InvalidOperationException("response too large");
                    }

                    var next = new byte[newLen];
                    Array.Copy(buffer, 0, next, 0, total);
                    buffer = next;
                }

                int n = stream.Read(buffer, total, buffer.Length - total);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            return Encoding.UTF8.GetString(buffer, 0, total);
        }

        private static GameInfo EmptyGame()
        {
            return new GameInfo { Status = GameStatus.None, Players = new PlayerInfo[0] };
        }

        private static int ClampEnum(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static int ClampByte(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }

        private sealed class GameInfoDto
        {
            public int Status { get; set; }
            public PlayerInfoDto[] Players { get; set; }
        }

        private sealed class PlayerInfoDto
        {
            public int DeviceId { get; set; }
            public string Name { get; set; }
            public string Team { get; set; }
            public int CombatScore { get; set; }
            public int EnemyFlagId { get; set; }
        }

        private sealed class PlayerSetupDto
        {
            public int DeviceId { get; set; }
        }

        private sealed class DeliverAcceptedDto
        {
            public bool Accepted { get; set; }
        }

        private sealed class RespawnScoreDto
        {
            public int DeviceId { get; set; }
            public int CombatScore { get; set; }
        }
    }
}
