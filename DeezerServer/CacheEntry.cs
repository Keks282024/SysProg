namespace DeezerServer
{
    internal class CacheEntry
    {
        public string Html { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public long SizeBytes { get; set; }
    }
}