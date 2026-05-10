using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace DeezerServer
{
    internal class Server
    {
        private readonly HttpListener _listener;

        private readonly Queue<ServerRequest> _pendingRequests = new Queue<ServerRequest>();
        private readonly object _queueSync = new object();

        private readonly List<Thread> _workerThreads = new List<Thread>();
        private readonly int _workerCount = 4;

        private volatile bool _running;
        private int _lastRequestId = 0;

        public Server(string prefix)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
        }

        public void Start()
        {
            _running = true;

            StartWorkers();

            _listener.Start();

            Logger.Info("Server sluša zahteve na http://localhost:5050/");
            Logger.Info($"Pokrenuto worker niti: {_workerCount}");

            while (_running)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    HandleClientContext(context);
                }
                catch (HttpListenerException)
                {
                    if (_running)
                    {
                        Logger.Error("Greška u radu HttpListener-a.");
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    Logger.Error("Greška u serveru: " + ex.Message);
                }
            }
        }

        public void Stop()
        {
            _running = false;

            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();

            lock (_queueSync)
            {
                Monitor.PulseAll(_queueSync);
            }

            foreach (Thread worker in _workerThreads)
            {
                worker.Join();
            }
        }

        private void StartWorkers()
        {
            for (int i = 1; i <= _workerCount; i++)
            {
                int workerId = i;

                Thread worker = new Thread(() => WorkerLoop(workerId));
                worker.Name = $"Worker {workerId}";
                worker.Start();

                _workerThreads.Add(worker);
            }
        }

        private void HandleClientContext(HttpListenerContext context)
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";

            if (path == "/stats")
            {
                SendResponse(
                    context,
                    ServerStats.ToHtml(),
                    "text/html; charset=utf-8",
                    200
                );
                return;
            }

            if (path == "/")
            {
                string html = """
                    <html>
                    <body>
                        <h1>Deezer Server radi</h1>
                        <form action="/search" method="get">
                            <label>Pretraga:</label>
                            <input type="text" name="q" />
                            <button type="submit">Traži</button>
                        </form>
                        <p>Primer: <a href="/search?q=coldplay">/search?q=coldplay</a></p>
                        <p><a href="/stats">Statistika servera</a></p>
                    </body>
                    </html>
                    """;

                SendResponse(context, html, "text/html; charset=utf-8", 200);
                return;
            }

            if (path != "/search")
            {
                SendResponse(
                    context,
                    "<h1>404 - Ruta nije pronađena</h1>",
                    "text/html; charset=utf-8",
                    404
                );
                return;
            }

            ServerStats.SearchRequest();

            string? query = context.Request.QueryString["q"];

            if (string.IsNullOrWhiteSpace(query))
            {
                SendResponse(
                    context,
                    "<h1>400 - Nedostaje q parametar</h1><p>Primer: /search?q=coldplay</p>",
                    "text/html; charset=utf-8",
                    400
                );
                return;
            }

            ServerRequest request = new ServerRequest
            {
                Id = Interlocked.Increment(ref _lastRequestId),
                Query = query,
                CreatedAt = DateTime.Now,
                Context = context
            };

            AddToQueue(request);
        }

        private void AddToQueue(ServerRequest request)
        {
            lock (_queueSync)
            {
                _pendingRequests.Enqueue(request);

                Logger.Info(
                    $"Request {request.Id} dodat u red. Query = {request.Query}. Trenutno u redu: {_pendingRequests.Count}"
                );

                Monitor.Pulse(_queueSync);
            }
        }

        private void WorkerLoop(int workerId)
        {
            Logger.Info($"Worker {workerId} je pokrenut.");

            while (true)
            {
                ServerRequest request;

                lock (_queueSync)
                {
                    while (_running && _pendingRequests.Count == 0)
                    {
                        Monitor.Wait(_queueSync);
                    }

                    if (!_running && _pendingRequests.Count == 0)
                    {
                        Logger.Info($"Worker {workerId} se zaustavlja.");
                        return;
                    }

                    request = _pendingRequests.Dequeue();

                    Logger.Info(
                        $"Worker {workerId} preuzeo Request {request.Id}. Preostalo u redu: {_pendingRequests.Count}"
                    );
                }

                ProcessRequest(workerId, request);
            }
        }

        private void ProcessRequest(int workerId, ServerRequest request)
        {
            try
            {
                Logger.Info(
                    $"Worker {workerId} obrađuje Request {request.Id}. Query = {request.Query}"
                );

                var result = DeezerService.SearchToHtml(request.Query, request.Id);

                SendResponse(
                    request.Context,
                    result.Html,
                    "text/html; charset=utf-8",
                    result.StatusCode
                );

                Logger.Info(
                    $"Worker {workerId} završio Request {request.Id}. Status = {result.StatusCode}"
                );
            }
            catch (Exception ex)
            {
                ServerStats.Error();

                Logger.Error(
                    $"Greška pri obradi Request {request.Id}: {ex.Message}"
                );

                string errorHtml = $"""
                    <html>
                    <body>
                        <h1>Greška prilikom obrade zahteva</h1>
                        <p>{WebUtility.HtmlEncode(ex.Message)}</p>
                    </body>
                    </html>
                    """;

                try
                {
                    SendResponse(
                        request.Context,
                        errorHtml,
                        "text/html; charset=utf-8",
                        500
                    );
                }
                catch
                {
                }
            }
        }

        private void SendResponse(
            HttpListenerContext context,
            string content,
            string contentType,
            int statusCode)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = buffer.Length;

            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
    }
}