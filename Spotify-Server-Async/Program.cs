using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace SpotifyApiServer
{
    class Program
    {
        private static readonly RequestQueue _requestQueue = new RequestQueue(50);
        private static readonly Cache _cache = new Cache(TimeSpan.FromSeconds(20));
        private static readonly SpotifyApiClient _spotifyClient = new SpotifyApiClient();

        private static readonly int _workerTaskCount = 5;


        private static readonly SemaphoreSlim _concurrencyControl = new SemaphoreSlim(_workerTaskCount, _workerTaskCount);


        private static bool _isRunning = true;

        private static readonly bool _useSystemTasks = false;


        static async Task Main(string[] args)
        {
            Logger.Log("Starting asynchronous server...");

            if (!_useSystemTasks)
            {
                Logger.Log($"[REGIME] Using custom dedicated worker Tasks (Count: {_workerTaskCount}).");
                for (int i = 0; i < _workerTaskCount; i++)
                {
                    _ = ConsumerLoopAsync();
                }
            }
            else
            {
                Logger.Log("[REGIME] Using dynamic system Tasks (Direct ThreadPool).");
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
            Console.WriteLine("Press any key to shutdown...");

            Thread shutdownThread = new Thread(() => WaitForShutdown(listener))
            {
                IsBackground = true,
                Name = "Thread-Shutdown"
            };
            shutdownThread.Start();


            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    Logger.Log($"Request received from: {context.Request.RemoteEndPoint}");

                    if (!_useSystemTasks)
                    {
                        await _requestQueue.EnqueueAsync(context);
                    }
                    else
                    {
                        Task processingTask = Task.Run(async () =>
                        {
                            await _concurrencyControl.WaitAsync();
                            try
                            {
                                await ProcessSingleRequestAsync(context);
                            }
                            finally
                            {
                                _concurrencyControl.Release();
                            }
                        });

                        _ = processingTask.ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                Logger.Log($"[CRITICAL] Unhandled error in system Task: {t.Exception?.GetBaseException().Message}");
                            }
                        });
                    }


                }
                catch (HttpListenerException)
                {
                    /* Expected on shutdown */
                }
                catch (Exception e)
                {
                    if (_isRunning) Logger.Log($"Error receiving request: {e.Message}");
                }
            }

        }

        private static async Task ProcessSingleRequestAsync(HttpListenerContext context)
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
                        await SendResponseAsync(response, 400, "{\"error\": \"Missing parameter 'q'\"}");
                        return;
                    }

                    string cacheKey = $"{q}_{type}_{limit}".ToLower();

                    string resultJson = await _cache.GetOrFetchAsync(cacheKey, async () =>
                    {
                        return await _spotifyClient.SearchAsync(q, type, limit);
                    });

                    await SendResponseAsync(response, 200, resultJson);
                }
                else
                {
                    await SendResponseAsync(response, 404, "{\"error\": \"Route not found. Use /search\"}");
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Error processing request: {e.Message}");
                if (e.Message.Contains("does not exist"))
                {
                    await SendResponseAsync(context.Response, 404, $"{{\"error\": \"{e.Message}\"}}");
                }
                else
                {
                    await SendResponseAsync(context.Response, 500, "{\"error\": \"Internal server error\"}");
                }
            }
            finally
            {
                stopwatch.Stop();
                Logger.Log($"[STATISTICS] Processing completed in: {stopwatch.ElapsedMilliseconds} ms");
            }


        }


        private static async Task ConsumerLoopAsync()
        {
            while (_isRunning)
            {
                HttpListenerContext? context = await _requestQueue.DequeueAsync();
                if (context == null)
                {
                    break;
                }

                await _concurrencyControl.WaitAsync();

                Task processingTask = ProcessSingleRequestAsync(context);

                _ = processingTask.ContinueWith(t =>
                {
                    _concurrencyControl.Release();
                    if (t.IsFaulted)
                    {
                        Logger.Log($"[CRITICAL] Unhandled error in dedicated Task: {t.Exception?.GetBaseException().Message}");
                    }
                });
            }
        }




        private static void WaitForShutdown(HttpListener listener)
        {
            Console.ReadKey();
            Logger.Log("Shutting down server...");

            _isRunning = false;
            listener.Stop();
            _requestQueue.Stop();

            Logger.Stop();
            Console.WriteLine("Server is shut down.");
        }


        private static async Task SendResponseAsync(HttpListenerResponse response, int statusCode, string responseBody)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(responseBody);
                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception e)
            {
                Logger.Log($"Error sending: {e.Message}");
            }
        }
    }
}