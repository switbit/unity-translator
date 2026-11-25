using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace UnityTranslator
{
    internal struct TranslationResult
    {
        public bool Success;
        public string Text;
        public string DetectedLanguage;
        public string Error;

        public static TranslationResult Ok(string text, string detected)
        {
            TranslationResult r = new TranslationResult();
            r.Success = true;
            r.Text = text;
            r.DetectedLanguage = detected;
            return r;
        }

        public static TranslationResult Fail(string error)
        {
            TranslationResult r = new TranslationResult();
            r.Success = false;
            r.Error = error;
            return r;
        }
    }

    internal interface ITranslator
    {
        string Name { get; }
        TranslationResult Translate(string text, string targetLanguage);
    }

    /// <summary>
    /// Serialises the start of requests across all worker threads and provides a
    /// global cool-down when a provider asks us to slow down.
    /// </summary>
    internal static class RateLimiter
    {
        private static readonly object Sync = new object();
        private static DateTime _nextRequestUtc = DateTime.MinValue;
        private static DateTime _pauseUntilUtc = DateTime.MinValue;

        public static void Wait(int delayMs)
        {
            if (delayMs < 0) delayMs = 0;
            while (true)
            {
                int sleepMs;
                lock (Sync)
                {
                    DateTime now = DateTime.UtcNow;
                    DateTime earliest = _pauseUntilUtc > _nextRequestUtc ? _pauseUntilUtc : _nextRequestUtc;
                    if (earliest <= now)
                    {
                        _nextRequestUtc = now.AddMilliseconds(delayMs);
                        return;
                    }
                    sleepMs = (int)Math.Min(1000, Math.Ceiling((earliest - now).TotalMilliseconds));
                }
                if (sleepMs > 0) Thread.Sleep(sleepMs);
            }
        }

        public static void Pause(int seconds)
        {
            if (seconds <= 0) return;
            lock (Sync)
            {
                DateTime until = DateTime.UtcNow.AddSeconds(seconds);
                if (until > _pauseUntilUtc) _pauseUntilUtc = until;
            }
        }
    }

    internal sealed class GoogleTranslator : ITranslator
    {
        private const int MaxEncodedChunk = 5200;

        // clients5 is the endpoint used by Chrome's dictionary extension. In practice
        // it is much more tolerant of short bursts than translate_a/single.
        private static readonly string[] Endpoints =
        {
            "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl={0}&q={1}",
            "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={0}&dt=t&q={1}",
            "https://translate.google.com/translate_a/single?client=gtx&sl=auto&tl={0}&dt=t&q={1}"
        };

        private readonly HttpClient _http;
        private int _preferredEndpoint;

        public string Name { get { return "Google"; } }

        static GoogleTranslator()
        {
            try
            {
                // TLS 1.2 on older Unity/Mono runtimes. The numeric value is used
                // because netstandard2.0 does not define SecurityProtocolType.Tls12.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch { }
        }

        public GoogleTranslator()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.UseCookies = false;
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        }

        public TranslationResult Translate(string text, string targetLanguage)
        {
            if (string.IsNullOrEmpty(text)) return TranslationResult.Ok(text ?? string.Empty, null);

            string prefix, core, suffix;
            SplitOuterWhitespace(text, out prefix, out core, out suffix);
            if (core.Length == 0) return TranslationResult.Ok(text, null);

            List<string> chunks = SplitChunks(core);
            StringBuilder translated = new StringBuilder(core.Length + 32);
            string detected = null;

            for (int i = 0; i < chunks.Count; i++)
            {
                TranslationResult part = TranslateChunk(chunks[i], targetLanguage);
                if (!part.Success) return part;
                if (detected == null && !string.IsNullOrEmpty(part.DetectedLanguage))
                    detected = part.DetectedLanguage;
                translated.Append(part.Text);
            }

            string result = prefix + translated + suffix;
            return TranslationResult.Ok(result, detected);
        }

        private TranslationResult TranslateChunk(string chunk, string targetLanguage)
        {
            string encoded = Uri.EscapeDataString(chunk);
            string encodedTarget = Uri.EscapeDataString(targetLanguage);
            Exception last = null;
            int totalAttempts = Endpoints.Length * 2;

            for (int attempt = 0; attempt < totalAttempts; attempt++)
            {
                int endpointIndex = (_preferredEndpoint + attempt) % Endpoints.Length;
                string url = string.Format(Endpoints[endpointIndex], encodedTarget, encoded);
                bool rateLimited = false;

                try
                {
                    using (HttpResponseMessage response = _http.GetAsync(url).GetAwaiter().GetResult())
                    {
                        if ((int)response.StatusCode == 429)
                        {
                            rateLimited = true;
                            last = new Exception("Google returned HTTP 429 (rate limit)");
                            RateLimiter.Pause(8);
                        }
                        else if (!response.IsSuccessStatusCode)
                        {
                            last = new Exception("Google returned HTTP " + (int)response.StatusCode);
                            if ((int)response.StatusCode >= 500) RateLimiter.Pause(3);
                        }
                        else
                        {
                            byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                            string json = Encoding.UTF8.GetString(bytes);

                            TranslationResult parsed = ParseGoogleResponse(json, chunk, targetLanguage);
                            if (parsed.Success)
                            {
                                _preferredEndpoint = endpointIndex;
                                return parsed;
                            }
                            last = new Exception(parsed.Error ?? "Empty response");
                        }
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                if (attempt + 1 < totalAttempts)
                {
                    int pause = rateLimited ? 0 : 500 * (attempt + 1);
                    if (rateLimited && (attempt + 1) % Endpoints.Length == 0) pause = 3000;
                    if (pause > 0) Thread.Sleep(pause);
                }
            }

            string message = last == null ? "unknown error" : last.Message;
            if (message.Length > 300) message = message.Substring(0, 300);
            return TranslationResult.Fail(message);
        }

        private static TranslationResult ParseGoogleResponse(string json, string source, string targetLanguage)
        {
            object root = Json.Parse(json);
            List<object> rootArray = JsonUtil.AsArray(root);
            if (rootArray == null || rootArray.Count == 0)
                return TranslationResult.Fail("Unexpected Google response");

            StringBuilder translated = new StringBuilder();
            string detected = null;
            bool parsed = false;

            // translate_a/single: [[[text, source, ...], ...], null, "en", ...]
            List<object> first = JsonUtil.AsArray(rootArray[0]);
            if (first != null && first.Count > 0 && JsonUtil.AsArray(first[0]) != null)
            {
                for (int i = 0; i < first.Count; i++)
                {
                    List<object> segment = JsonUtil.AsArray(first[i]);
                    if (segment == null || segment.Count == 0) continue;
                    string value = JsonUtil.AsString(segment[0]);
                    if (value != null)
                    {
                        translated.Append(value);
                        parsed = true;
                    }
                }
                detected = rootArray.Count > 2 ? JsonUtil.AsString(rootArray[2]) : null;
            }
            else
            {
                // clients5: [["translated text", "en"]]
                for (int i = 0; i < rootArray.Count; i++)
                {
                    List<object> pair = JsonUtil.AsArray(rootArray[i]);
                    if (pair != null && pair.Count > 0)
                    {
                        string value = JsonUtil.AsString(pair[0]);
                        if (value != null)
                        {
                            translated.Append(value);
                            parsed = true;
                        }
                        string partDetected = pair.Count > 1 ? JsonUtil.AsString(pair[1]) : null;
                        if (!string.IsNullOrEmpty(partDetected)) detected = partDetected;
                    }
                    else
                    {
                        string value = JsonUtil.AsString(rootArray[i]);
                        if (value != null)
                        {
                            translated.Append(value);
                            parsed = true;
                        }
                    }
                }
            }

            if (!parsed)
                return TranslationResult.Fail("Google response has no translation segments");

            string result = translated.ToString();
            if (result.Length == 0 && source.Length > 0)
                return TranslationResult.Fail("Google returned an empty translation");

            if (!string.IsNullOrEmpty(detected) && !string.IsNullOrEmpty(targetLanguage))
            {
                string targetBase = BaseLanguage(targetLanguage);
                string detectedBase = BaseLanguage(detected);
                if (string.Equals(targetBase, detectedBase, StringComparison.OrdinalIgnoreCase))
                    result = source;
            }

            return TranslationResult.Ok(result, detected);
        }

        private static string BaseLanguage(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            int dash = code.IndexOf('-');
            return dash > 0 ? code.Substring(0, dash) : code;
        }

        private static void SplitOuterWhitespace(string text, out string prefix, out string core, out string suffix)
        {
            int start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            int end = text.Length - 1;
            while (end >= start && char.IsWhiteSpace(text[end])) end--;
            prefix = text.Substring(0, start);
            suffix = text.Substring(end + 1);
            core = text.Substring(start, end - start + 1);
        }

        private static List<string> SplitChunks(string text)
        {
            List<string> chunks = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int end = FindChunkEnd(text, start);
                if (end <= start) end = Math.Min(text.Length, start + 1);
                chunks.Add(text.Substring(start, end - start));
                start = end;
            }
            if (chunks.Count == 0) chunks.Add(text);
            return chunks;
        }

        private static int FindChunkEnd(string text, int start)
        {
            int encoded = 0;
            int i = start;
            int hardEnd = start;
            while (i < text.Length)
            {
                int add = EncodedCharLength(text[i]);
                if (encoded + add > MaxEncodedChunk || i - start >= 2000) break;
                encoded += add;
                i++;
                hardEnd = i;
            }

            if (hardEnd >= text.Length) return text.Length;
            if (hardEnd <= start) return Math.Min(text.Length, start + 1);

            int minimum = start + Math.Max(1, (hardEnd - start) / 2);
            for (int j = hardEnd; j > minimum; j--)
            {
                char c = text[j - 1];
                if (c == '\n') return j;
            }
            for (int j = hardEnd; j > minimum; j--)
            {
                char c = text[j - 1];
                if (c == '.' || c == '!' || c == '?' || c == ';' ||
                    c == '\u3002' || c == '\uFF01' || c == '\uFF1F')
                {
                    if (j < text.Length && char.IsWhiteSpace(text[j])) return j + 1;
                    return j;
                }
            }
            for (int j = hardEnd; j > minimum; j--)
            {
                if (char.IsWhiteSpace(text[j - 1])) return j;
            }

            // Do not split a surrogate pair.
            if (hardEnd > start + 1 && char.IsHighSurrogate(text[hardEnd - 1])) hardEnd--;
            return hardEnd;
        }

        private static int EncodedCharLength(char c)
        {
            if (c < 128) return 1;
            if (c < 0x800) return 6;
            return 9;
        }
    }

    internal sealed class LibreTranslator : ITranslator
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _apiKey;

        public string Name { get { return "LibreTranslate"; } }

        public LibreTranslator(string endpoint, string apiKey)
        {
            _endpoint = (endpoint ?? string.Empty).TrimEnd('/');
            _apiKey = apiKey ?? string.Empty;
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public TranslationResult Translate(string text, string targetLanguage)
        {
            if (string.IsNullOrEmpty(text)) return TranslationResult.Ok(text ?? string.Empty, null);
            if (string.IsNullOrEmpty(_endpoint)) return TranslationResult.Fail("LibreTranslate URL is empty");

            string body = "{\"q\":" + Json.Escape(text) +
                          ",\"source\":\"auto\",\"target\":" + Json.Escape(targetLanguage) +
                          ",\"format\":\"text\"";
            if (_apiKey.Length > 0)
                body += ",\"api_key\":" + Json.Escape(_apiKey);
            body += "}";

            Exception last = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (StringContent content = new StringContent(body, Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = _http.PostAsync(_endpoint + "/translate", content)
                                                               .GetAwaiter().GetResult())
                    {
                        byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                        string json = Encoding.UTF8.GetString(bytes);
                        if (!response.IsSuccessStatusCode)
                        {
                            last = new Exception("LibreTranslate returned HTTP " + (int)response.StatusCode +
                                                 ": " + Trim(json, 200));
                        }
                        else
                        {
                            object root = Json.Parse(json);
                            string translated = JsonUtil.GetString(root, "translatedText");
                            if (translated == null)
                            {
                                last = new Exception("LibreTranslate response has no translatedText");
                            }
                            else
                            {
                                string detected = JsonUtil.GetString(JsonUtil.Get(root, "detectedLanguage"), "language");
                                return TranslationResult.Ok(translated, detected);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                Thread.Sleep(700);
            }

            return TranslationResult.Fail(last == null ? "unknown error" : Trim(last.Message, 300));
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max);
        }
    }
}
