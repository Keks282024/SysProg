using System;
using System.Net;

namespace DeezerServer
{
    internal class ServerRequest
    {
        public int Id { get; set; }
        public string Query { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public HttpListenerContext Context { get; set; } = null!;
    }
}