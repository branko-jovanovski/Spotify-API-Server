using System;
using System.IO;
using System.Threading;

namespace SpotifyApiServer
{
    public static class Logger
    {
        private static readonly object _fileLock = new object();

        private static readonly string _filePath = Path.Combine(Directory.GetCurrentDirectory(), "server_activity.txt");

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

            int threadId = Thread.CurrentThread.ManagedThreadId;

            string logEntry = $"[{timestamp}] [Thread ID: {threadId:D2}] {message}";

            try
            {
                lock (_fileLock)
                {
                    Console.WriteLine(logEntry);
                    File.AppendAllText(_filePath, logEntry + Environment.NewLine);
                }
            }
            catch (Exception e)
            {
                lock (_fileLock)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[{timestamp}] ERROR (Thread ID: {threadId:D2}) : Error writing to file: {e.Message}");
                    Console.ResetColor();
                }
            }
        }
    }
}