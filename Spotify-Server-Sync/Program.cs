using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace SpotifyApiServer
{
    class Program
    {
        private static readonly RequestQueue _requestQueue = new RequestQueue(50);
        private static readonly Cache _cache = new Cache(TimeSpan.FromSeconds(20));
        private static readonly SpotifyApiClient _spotifyClient = new SpotifyApiClient();

        private static readonly int workerCount = 5;
        private static bool _isRunning = true;
        private static readonly bool useSystemThreadPool = false;

        static void Main(string[] args)
        {
            Logger.Log("Starting server...");

            if (!useSystemThreadPool)
            {
                Logger.Log("[REGIME] Using custom threads.");
                for (int i = 0; i < workerCount; i++)
                {
                    Thread worker = new Thread(ProcessRequestsLoop)
                    {
                        IsBackground = true,
                        Name = $"workerThread-{i + 1}"
                    };
                    worker.Start();
                }
            }
            else
            {
                Logger.Log("[REGIME] Using ThreadPool.");
                ThreadPool.SetMaxThreads(workerCount, workerCount);
            }

            using HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8080/");

            try
            {
                listener.Start();
            }
            catch (Exception e)
            {
                Logger.Log($"[FATAL] Error starting server: {e.Message}");
                return;
            }

            Logger.Log("Server listening on http://localhost:8080/");
            Logger.Log("Example query: http://localhost:8080/search?q=Taylor+Swift&type=track&limit=5");
            Console.WriteLine("Click any key to shutdown...");

            Thread shutdownThread = new Thread(() => WaitForShutdown(listener));
            shutdownThread.IsBackground = true;
            shutdownThread.Start();

            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    Logger.Log($"Request received from: {context.Request.RemoteEndPoint}");

                    _requestQueue.Enqueue(context);

                    if (useSystemThreadPool)
                    {
                        ThreadPool.QueueUserWorkItem(state =>
                        {
                            var ctx = _requestQueue.Dequeue();
                            if (ctx != null)
                            {
                                ProcessSingleRequest(ctx);
                            }
                        });
                    }
                }
                catch (Exception e)
                {
                    if (_isRunning) Logger.Log($"Error receiving request: {e.Message}");
                }
            }
        }

        private static void WaitForShutdown(HttpListener listener)
        {
            Console.ReadKey();
            Logger.Log("Shutting down server...");
            _isRunning = false;
            listener.Stop();
            _requestQueue.Stop();
            Logger.Log("Server shutdown.");
        }

        private static void ProcessRequestsLoop()
        {
            while (_isRunning)
            {
                HttpListenerContext? context = _requestQueue.Dequeue();
                if (context == null)
                {
                    break;
                }
                ProcessSingleRequest(context);
            }
        }

        private static void ProcessSingleRequest(HttpListenerContext context)
        {

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/search")
                {
                    string? q = request.QueryString["q"];
                    string type = request.QueryString["type"] ?? "track";
                    string limit = request.QueryString["limit"] ?? "5";

                    if (string.IsNullOrWhiteSpace(q))
                    {
                        SendResponse(response, 400, "{\"error\": \"Missing parameter 'q'\"}");
                        return;
                    }

                    string cacheKey = $"{q}_{type}_{limit}".ToLower();

                    string resultJson = _cache.GetOrFetch(cacheKey, () =>
                    {
                        return _spotifyClient.Search(q, type, limit);
                    });

                    SendResponse(response, 200, resultJson);
                }
                else
                {
                    SendResponse(response, 404, "{\"error\": \"Route not found. Use /search\"}");
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Error processing request: {e.Message}");
                if (e.Message.Contains("does not exist"))
                {
                    SendResponse(context.Response, 404, $"{{\"error\": \"{e.Message}\"}}");
                }
                else
                {
                    SendResponse(context.Response, 500, "{\"error\": \"Internal server error\"}");
                }
            }
            finally
            {
                stopwatch.Stop();
                Logger.Log($"[STATISTICS] Processing completed in: {stopwatch.ElapsedMilliseconds} ms");
            }
        }

        private static void SendResponse(HttpListenerResponse response, int statusCode, string responseBody)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(responseBody);
                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                using Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception e)
            {
                Logger.Log($"Error sending: {e.Message}");
            }
        }
    }
}