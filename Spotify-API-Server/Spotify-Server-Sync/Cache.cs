using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SpotifyApiServer
{

    public class Cache
    {
        private class CacheItem
        {
            public string Data { get; set; } = null!;
            public DateTime ExpirationTime { get; set; }
            public bool IsFetching { get; set; } = false;
            public object Lock { get; } = new object();

        }


        private readonly Dictionary<string, CacheItem> _storage = new Dictionary<string, CacheItem>();
        private readonly object _lock = new object();
        private readonly TimeSpan _timeToLive;

        public Cache(TimeSpan ttl)
        {
            _timeToLive = ttl;


            Thread cleanerThread = new Thread(ActiveCacheCleanup)
            {
                IsBackground = true,
                Name = "Thread-CacheCleaner"
            };
            cleanerThread.Start();
        }

        private void ActiveCacheCleanup()
        {
            while (true)
            {
                Thread.Sleep(120000);

                int removedCount = 0;

                lock (_lock)
                {
                    DateTime now = DateTime.Now;

                    List<string> keysToRemove = new List<string>();

                    foreach (var par in _storage)
                    {
                        if (now >= par.Value.ExpirationTime && !par.Value.IsFetching)
                        {
                            keysToRemove.Add(par.Key);
                        }
                    }

                    foreach (var k in keysToRemove)
                    {
                        _storage.Remove(k);
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    Logger.Log($"[CLEANER] Removed {removedCount} expired item(s) from cache.");
                }
            }
        }

        public string GetOrFetch(string key, Func<string> fetchFunction)
        {
            CacheItem item;

            lock (_lock)
            {
                if (!_storage.TryGetValue(key, out var tempItem))
                {
                    tempItem = new CacheItem();
                    _storage[key] = tempItem;
                    Logger.Log($"[CACHE MISS] Created new entry for : {key}");
                }

                item = tempItem;
            }

            lock (item.Lock)
            {
                if (item.Data != null && DateTime.Now < item.ExpirationTime)
                {
                    Logger.Log($"[CACHE HIT] Data found for : {key}");
                    return item.Data;
                }

                if (item.IsFetching)
                {
                    Logger.Log($"[STAMPEDE PREVENTION] Thread waiting for result for : {key}");
                    while (item.IsFetching)
                    {
                        Monitor.Wait(item.Lock);
                    }
                    return item.Data!;
                }

                item.IsFetching = true;
            }

            string fetchedResult = null!;
            try
            {
                Logger.Log($"[API FETCH] Fetching from API for key : {key}");
                fetchedResult = fetchFunction();
            }
            catch (Exception e)
            {
                Logger.Log($"[API ERROR] Error fetching data for key {key} : {e.Message}");
                lock (_lock) { _storage.Remove(key); }
                throw;
            }
            finally
            {
                lock (item.Lock)
                {
                    if (fetchedResult != null)
                    {
                        item.Data = fetchedResult;
                        item.ExpirationTime = DateTime.Now.Add(_timeToLive);
                    }
                    item.IsFetching = false;
                    Monitor.PulseAll(item.Lock);
                }
            }

            return fetchedResult;
        }
    }
}