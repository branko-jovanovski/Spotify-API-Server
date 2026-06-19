using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace SpotifyApiServer
{
    public static class Logger
    {
        private static readonly string _filePath = Path.Combine(Directory.GetCurrentDirectory(), "server_activity.txt");

        private static readonly BlockingCollection<string> _logQueue = new BlockingCollection<string>();

        private static readonly Thread _writerThread;

        static Logger()
        {
            _writerThread = new Thread(WriteToFileFromQueue)
            {
                IsBackground = true,
                Name = "Thread-Logging"
            };
            _writerThread.Start();
        }

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

            int threadId = Thread.CurrentThread.ManagedThreadId;

            string taskInfo;

            if (Task.CurrentId != null)
            {
                taskInfo = "[Task: " + Task.CurrentId.Value.ToString("D2") + "]";
            }
            else
            {
                taskInfo = "[Thread]";
            }

            string logEntry = $"[{timestamp}] [Thread: {threadId:D2}] {taskInfo} {message}";

            try
            {
                if (!_logQueue.IsAddingCompleted)
                {
                    _logQueue.TryAdd(logEntry);
                }
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[SYSTEM ERROR] Failed to log: {e.Message}");
                Console.ResetColor();
            }
        }

        private static void WriteToFileFromQueue()
        {
            foreach (var message in _logQueue.GetConsumingEnumerable())
            {
                try
                {
                    Console.WriteLine(message);

                    File.AppendAllText(_filePath, message + Environment.NewLine);
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR : Error writing to file : {e.Message}");
                    Console.ResetColor();
                }
            }
        }

        public static void Stop()
        {
            _logQueue.CompleteAdding();

            _writerThread.Join(1000);
        }
    }
}