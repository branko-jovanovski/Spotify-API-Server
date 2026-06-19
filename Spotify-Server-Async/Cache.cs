using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;

namespace SpotifyApiServer
{
    public class CacheItem
    {
        public Task<string> FetchTask { get; set; } = null!;
        public DateTime ExpirationTime { get; set; }
    }

    public class Cache
    {
        private readonly Dictionary<string, CacheItem> _storage = new Dictionary<string, CacheItem>();

        private readonly ReaderWriterLockSlim _cacheLock = new ReaderWriterLockSlim();

        private readonly TimeSpan _timeToLive;

        private const int MaxCapacity = 100;

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

                _cacheLock.EnterWriteLock();
                try
                {
                    DateTime now = DateTime.Now;

                    var itemsToRemove = _storage.Where(pair => now >= pair.Value.ExpirationTime).ToList();

                    foreach (var pair in itemsToRemove)
                    {
                        _storage.Remove(pair.Key);

                        removedCount++;
                    }


                }
                finally
                {
                    _cacheLock.ExitWriteLock();
                }

                if (removedCount > 0)
                {
                    Logger.Log($"[CLEANER] Removed {removedCount} expired item(s) from cache.");
                }
            }
        }

        public async Task<string> GetOrFetchAsync(string key, Func<Task<string>> fetchFunction)
        {
            Task<string>? taskToWait = null;
            bool foundInCache = false;

            _cacheLock.EnterReadLock();
            try
            {
                if (_storage.TryGetValue(key, out var item) && DateTime.Now < item.ExpirationTime)
                {
                    taskToWait = item.FetchTask;
                    foundInCache = true;
                }
            }
            finally
            {
                _cacheLock.ExitReadLock();
            }

            if (foundInCache)
            {
                Logger.Log($"[CACHE HIT] Data found in cache for: {key}");
                return await taskToWait!;
            }

            _cacheLock.EnterWriteLock();
            try
            {
                if (_storage.TryGetValue(key, out var item) && DateTime.Now < item.ExpirationTime)
                {
                    taskToWait = item.FetchTask;
                }
                else
                {
                    Logger.Log($"[CACHE MISS] Creating new fetch for: {key}");

                    if (_storage.Count >= MaxCapacity)
                    {
                        string? keyToRemove = null;
                        DateTime earliestExpirationTime = DateTime.MaxValue;

                        foreach (var pair in _storage)
                        {
                            if (pair.Value.ExpirationTime < earliestExpirationTime)
                            {
                                earliestExpirationTime = pair.Value.ExpirationTime;
                                keyToRemove = pair.Key;
                            }
                        }

                        if (keyToRemove != null)
                        {
                            _storage.Remove(keyToRemove);
                            Logger.Log($"[CACHE OVERFLOW] Cache has reached the limit of {MaxCapacity} items. Evicted item: {keyToRemove}");
                        }
                    }

                    taskToWait = FetchAndStoreAsync(key, fetchFunction);

                    _storage[key] = new CacheItem
                    {
                        FetchTask = taskToWait,
                        ExpirationTime = DateTime.Now.Add(_timeToLive)
                    };
                }
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }

            return await taskToWait!;
        }

        private async Task<string> FetchAndStoreAsync(string key, Func<Task<string>> fetchFunction)
        {
            try
            {
                return await fetchFunction();
            }
            catch (Exception e)
            {
                Logger.Log($"[API ERROR] Error fetching data for {key}: {e.Message}");

                _cacheLock.EnterWriteLock();
                try { _storage.Remove(key); }
                finally { _cacheLock.ExitWriteLock(); }

                throw;
            }
        }
    }
}