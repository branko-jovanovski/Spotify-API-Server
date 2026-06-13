using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace SpotifyApiServer
{
    public class RequestQueue
    {
        private readonly Queue<HttpListenerContext> _queue = new Queue<HttpListenerContext>();
        private readonly int _maxCapacity;
        private readonly object _lock = new object();
        private bool _isStopping = false;

        public RequestQueue(int capacity = 100)
        {
            _maxCapacity = capacity;
        }

        public void Enqueue(HttpListenerContext context)
        {
            lock (_lock)
            {
                while (_queue.Count >= _maxCapacity && !_isStopping)
                {
                    Logger.Log("[QUEUE] Queue is full, main thread is waiting...");
                    Monitor.Wait(_lock);
                }

                if (_isStopping) return;

                _queue.Enqueue(context);
                Logger.Log($"[QUEUE] Request added. Current count: {_queue.Count}");

                Monitor.Pulse(_lock);
            }
        }

        public HttpListenerContext? Dequeue()
        {
            lock (_lock)
            {
                while (_queue.Count == 0 && !_isStopping)
                {
                    Monitor.Wait(_lock);
                }

                if (_isStopping && _queue.Count == 0)
                    return null;

                HttpListenerContext context = _queue.Dequeue();

                Monitor.Pulse(_lock);
                return context;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _isStopping = true;
                Monitor.PulseAll(_lock);
                Logger.Log("[QUEUE] Stop signal sent to all threads.");
            }
        }
    }
}