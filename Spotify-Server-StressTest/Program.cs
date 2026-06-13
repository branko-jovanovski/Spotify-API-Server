using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static readonly HttpClient client = new HttpClient();
    private static readonly string ServerUrl = "http://localhost:8080/search";

    static async Task Main()
    {
        Console.WriteLine("Press ENTER to start the stress test...");
        Console.ReadLine();

        await RunTest("TEST 1: Cache Stampede", new[] { "Taylor Swift" }, 50, 50);

        string[] queries = { "Queen", "Drake", "Eminem", "Rihanna", "Adele", "Metallica" };
        await RunTest("TEST 2: Different queries", queries, 1000, 100);

        Console.WriteLine("\n=== STRESS TEST COMPLETED ===");
        Console.ReadLine();
    }

    static async Task RunTest(string testName, string[] queries, int totalRequests, int maxConcurrent)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n{testName}");
        Console.ResetColor();

        var sw = Stopwatch.StartNew();
        var sem = new SemaphoreSlim(maxConcurrent);
        var tasks = new List<Task>();
        var rnd = new Random();

        int successCount = 0;
        int errorCount = 0;

        for (int i = 0; i < totalRequests; i++)
        {
            await sem.WaitAsync();

            string query = queries[rnd.Next(queries.Length)];
            string url = $"{ServerUrl}?q={Uri.EscapeDataString(query)}";

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var res = await client.GetAsync(url);

                    if (res.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref errorCount);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                }
                finally
                {
                    sem.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        if (errorCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
        }

        Console.WriteLine($"Time taken: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Successful requests: {successCount}");
        Console.WriteLine($"Errors: {errorCount}");
        Console.ResetColor();

    }
}