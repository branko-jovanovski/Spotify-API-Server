using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyApiServer
{
    public class RequestQueue
    {
        private readonly ConcurrentQueue<HttpListenerContext> _queue = new ConcurrentQueue<HttpListenerContext>();

        private readonly SemaphoreSlim _freeSlots;
        private readonly SemaphoreSlim _availableRequests = new SemaphoreSlim(0);

        private bool _isStopping = false;

        public RequestQueue(int capacity = 100)
        {
            _freeSlots = new SemaphoreSlim(capacity, capacity);
        }

        public async Task EnqueueAsync(HttpListenerContext context)
        {
            if (_isStopping)
            {
                return;
            }

            await _freeSlots.WaitAsync();

            if (_isStopping)
            {
                _freeSlots.Release();
                return;
            }

            _queue.Enqueue(context);
            Logger.Log($"[QUEUE] Request added to the queue. Currently waiting: {_queue.Count}");

            _availableRequests.Release();
        }

        public async Task<HttpListenerContext?> DequeueAsync()
        {
            while (!_isStopping)
            {
                bool hasRequests = await _availableRequests.WaitAsync(1000);

                if (_isStopping)
                {
                    return null;
                }

                if (hasRequests)
                {
                    if (_queue.TryDequeue(out var context))
                    {
                        _freeSlots.Release();
                        return context;
                    }
                }
            }
            return null;
        }

        public void Stop()
        {
            _isStopping = true;
            Logger.Log("[QUEUE] Stop signal sent, worker Tasks are shutting down.");
        }
    }
}