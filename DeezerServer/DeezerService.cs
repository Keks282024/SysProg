using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeezerServer
{
    internal static class DeezerService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

        private static readonly Queue<string> _cacheInsertOrder = new Queue<string>();

        private static readonly HashSet<string> _queriesInProgress = new HashSet<string>();

        private static readonly object _cacheSync = new object();

        private const long MaxCacheBytes = 2 * 1024 * 1024;

        private static long _currentCacheBytes = 0;

        public static (string Html, int StatusCode) SearchToHtml(string query, int requestId)
        {
            string cacheKey = BuildCacheKey(query);

            lock (_cacheSync)
            {
                while (true)
                {
                    if (_cache.TryGetValue(cacheKey, out CacheEntry cachedItem))
                    {
                        Logger.Info($"Request {requestId} CACHE HIT: {cacheKey}");
                        ServerStats.CacheHit();
                        return (cachedItem.Html, cachedItem.StatusCode);
                    }

                    if (!_queriesInProgress.Contains(cacheKey))
                    {
                        _queriesInProgress.Add(cacheKey);

                        Logger.Info($"Request {requestId} CACHE MISS: {cacheKey}");
                        ServerStats.CacheMiss();
                        Logger.Info($"Request {requestId} starts loading result: {cacheKey}");

                        break;
                    }

                    Logger.Info($"Request {requestId} waits because query is already being loaded: {cacheKey}");
                    ServerStats.WaitingForResult();
                    Monitor.Wait(_cacheSync);
                }
            }

            try
            {
                Logger.Info($"Request {requestId} FETCH START: {cacheKey}");

                ServerStats.DeezerCall();

                string json = CallDeezerAsync(query).GetAwaiter().GetResult();

                var result = ConvertJsonToHtml(json, query);

                lock (_cacheSync)
                {
                    StoreInCache(cacheKey, result.Html, result.StatusCode, requestId);

                    _queriesInProgress.Remove(cacheKey);
                    Monitor.PulseAll(_cacheSync);
                }

                Logger.Info($"Request {requestId} FETCH SUCCESS: {cacheKey}");

                return result;
            }
            catch
            {
                lock (_cacheSync)
                {
                    _queriesInProgress.Remove(cacheKey);
                    Monitor.PulseAll(_cacheSync);
                }

                ServerStats.Error();

                Logger.Error($"Request {requestId} FETCH FAILED: {cacheKey}");

                throw;
            }
        }

        private static void StoreInCache(string cacheKey, string html, int statusCode, int requestId)
        {
            long itemSize = Encoding.UTF8.GetByteCount(html);

            if (itemSize > MaxCacheBytes)
            {
                Logger.Info(
                    $"Request {requestId} result too large for cache: {cacheKey}, size = {itemSize} B"
                );

                ServerStats.CacheEviction();

                return;
            }

            if (_cache.TryGetValue(cacheKey, out CacheEntry oldItem))
            {
                _currentCacheBytes -= oldItem.SizeBytes;
                _cache.Remove(cacheKey);
            }

            while (_currentCacheBytes + itemSize > MaxCacheBytes && _cacheInsertOrder.Count > 0)
            {
                string oldestKey = _cacheInsertOrder.Dequeue();

                if (_cache.TryGetValue(oldestKey, out CacheEntry oldestItem))
                {
                    _cache.Remove(oldestKey);
                    _currentCacheBytes -= oldestItem.SizeBytes;

                    Logger.Info(
                        $"CACHE EVICT FIFO: {oldestKey}, freed {oldestItem.SizeBytes} B"
                    );
                }
            }

            _cache[cacheKey] = new CacheEntry
            {
                Html = html,
                StatusCode = statusCode,
                SizeBytes = itemSize
            };

            _cacheInsertOrder.Enqueue(cacheKey);
            _currentCacheBytes += itemSize;

            Logger.Info(
                $"Request {requestId} CACHE STORED: {cacheKey}, size = {itemSize} B, total = {_currentCacheBytes} B"
            );

            ServerStats.SetCacheState(_cache.Count, _currentCacheBytes);
        }

        private static string BuildCacheKey(string query)
        {
            return query.Trim().ToLowerInvariant();
        }

        private static async Task<string> CallDeezerAsync(string query)
        {
            string encodedQuery = Uri.EscapeDataString(query);
            string deezerUrl = "https://api.deezer.com/search?q=" + encodedQuery;

            return await _httpClient.GetStringAsync(deezerUrl);
        }

        private static (string Html, int StatusCode) ConvertJsonToHtml(string json, string query)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return (
                    "<h1>Greška</h1><p>Deezer API nije vratio očekivani format odgovora.</p>",
                    500
                );
            }

            if (data.GetArrayLength() == 0)
            {
                string noResultsHtml = $"""
                    <html>
                    <body>
                        <h1>Nema rezultata</h1>
                        <p>Nema rezultata za: <b>{WebUtility.HtmlEncode(query)}</b></p>
                    </body>
                    </html>
                    """;

                return (noResultsHtml, 404);
            }

            StringBuilder html = new StringBuilder();

            html.Append("""
                <html>
                <head>
                    <meta charset="utf-8">
                    <title>Deezer rezultati</title>
                </head>
                <body>
                """);

            html.Append("<h1>Rezultati za: ");
            html.Append(WebUtility.HtmlEncode(query));
            html.Append("</h1>");

            html.Append("<ul>");

            int resultCount = 0;

            foreach (JsonElement track in data.EnumerateArray())
            {
                if (resultCount >= 10)
                {
                    break;
                }

                string title = ReadString(track, "title", "Nepoznata pesma");

                string artist = "Nepoznat izvođač";
                if (track.TryGetProperty("artist", out JsonElement artistElement))
                {
                    artist = ReadString(artistElement, "name", "Nepoznat izvođač");
                }

                string album = "Nepoznat album";
                if (track.TryGetProperty("album", out JsonElement albumElement))
                {
                    album = ReadString(albumElement, "title", "Nepoznat album");
                }

                string link = ReadString(track, "link", "");

                html.Append("<li>");
                html.Append("<b>");
                html.Append(WebUtility.HtmlEncode(title));
                html.Append("</b>");

                html.Append(" - ");
                html.Append(WebUtility.HtmlEncode(artist));

                html.Append(" | Album: ");
                html.Append(WebUtility.HtmlEncode(album));

                if (!string.IsNullOrWhiteSpace(link))
                {
                    html.Append(" | ");
                    html.Append("<a href=\"");
                    html.Append(WebUtility.HtmlEncode(link));
                    html.Append("\" target=\"_blank\">Otvori na Deezer-u</a>");
                }

                html.Append("</li>");

                resultCount++;
            }

            html.Append("</ul>");
            html.Append("</body></html>");

            return (html.ToString(), 200);
        }

        private static string ReadString(JsonElement element, string propertyName, string fallback)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property))
            {
                return fallback;
            }

            string? value = property.GetString();

            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}