using System.Text;
using System.Threading;

namespace DeezerServer
{
    internal static class ServerStats
    {
        private static int _totalSearchRequests;
        private static int _cacheHits;
        private static int _cacheMisses;
        private static int _deezerCalls;
        private static int _waitingRequests;
        private static int _cacheEvictions;
        private static int _errors;

        private static int _cacheItems;
        private static long _cacheBytes;

        public static void SearchRequest()
        {
            Interlocked.Increment(ref _totalSearchRequests);
        }

        public static void CacheHit()
        {
            Interlocked.Increment(ref _cacheHits);
        }

        public static void CacheMiss()
        {
            Interlocked.Increment(ref _cacheMisses);
        }

        public static void DeezerCall()
        {
            Interlocked.Increment(ref _deezerCalls);
        }

        public static void WaitingForResult()
        {
            Interlocked.Increment(ref _waitingRequests);
        }

        public static void CacheEviction()
        {
            Interlocked.Increment(ref _cacheEvictions);
        }

        public static void Error()
        {
            Interlocked.Increment(ref _errors);
        }

        public static void SetCacheState(int itemCount, long bytes)
        {
            Interlocked.Exchange(ref _cacheItems, itemCount);
            Interlocked.Exchange(ref _cacheBytes, bytes);
        }

        public static string ToHtml()
        {
            StringBuilder html = new StringBuilder();

            html.Append("""
                <html>
                <head>
                    <meta charset="utf-8">
                    <title>Server statistika</title>
                </head>
                <body>
                    <h1>Deezer server statistika</h1>
                    <ul>
                """);

            html.Append($"<li>Ukupno search zahteva: {Volatile.Read(ref _totalSearchRequests)}</li>");
            html.Append($"<li>Cache hit: {Volatile.Read(ref _cacheHits)}</li>");
            html.Append($"<li>Cache miss: {Volatile.Read(ref _cacheMisses)}</li>");
            html.Append($"<li>Deezer API pozivi: {Volatile.Read(ref _deezerCalls)}</li>");
            html.Append($"<li>Zahtevi koji su čekali isti upit: {Volatile.Read(ref _waitingRequests)}</li>");
            html.Append($"<li>Cache izbacivanja: {Volatile.Read(ref _cacheEvictions)}</li>");
            html.Append($"<li>Greške: {Volatile.Read(ref _errors)}</li>");
            html.Append($"<li>Broj elemenata u cache-u: {Volatile.Read(ref _cacheItems)}</li>");
            html.Append($"<li>Trenutna veličina cache-a: {Volatile.Read(ref _cacheBytes)} B</li>");

            html.Append("""
                    </ul>
                    <p><a href="/">Nazad</a></p>
                </body>
                </html>
                """);

            return html.ToString();
        }
    }
}