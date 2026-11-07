using System.Diagnostics;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The <c>service</c> command: a closed-loop load test of the service-style request (decode a 1024x768 JPEG, fit it in
/// 320x240, encode a JPEG) with 1, 2, 4... concurrent callers. Reports throughput, latency percentiles (p50/p95/p99/max),
/// managed allocations per request, GC counts and the process peak working set. BenchmarkDotNet measures means; tail
/// latencies need every individual request, hence this separate command.
/// </summary>
internal static class ServiceLoad
{
    public static void Run(string[] args)
    {
        var duration = TimeSpan.FromSeconds(GetOption(args, "--seconds", 10));
        var maxWorkers = GetOption(args, "--max-workers", Environment.ProcessorCount);
        var size = new Size(320, 240);
        var input = BenchmarkInputs.ServiceJpeg;

        // Warm-up (JIT, pools, lazy inputs)
        using (var warmup = new MemoryStream())
        {
            for (var i = 0; i < 20; i++)
            {
                ImageWorkloads.JpegDecodeResizeEncode(input, size, warmup);
            }
        }

        Console.WriteLine($"Service load: {RuntimeInformationText()}");
        Console.WriteLine($"Request: JPEG {BenchmarkInputs.ServiceWidth}x{BenchmarkInputs.ServiceHeight} q85 4:2:0 ({input.Length:N0} bytes) -> Rgb24 -> fit {size.Width}x{size.Height} (Catmull-Rom) -> JPEG q85; MaxDegreeOfParallelism = 1 per image; {duration.TotalSeconds:0} s per run");
        Console.WriteLine();
        Console.WriteLine("| Workers | Requests | Req/s | p50 (ms) | p95 (ms) | p99 (ms) | Max (ms) | Alloc/req | Gen0 | Gen1 | Gen2 | Peak working set |");
        Console.WriteLine("|--------:|---------:|------:|---------:|---------:|---------:|---------:|----------:|-----:|-----:|-----:|-----------------:|");
        for (var workers = 1; workers <= maxWorkers; workers *= 2)
        {
            RunOnce(workers, duration, input, size);
        }
    }

    private static void RunOnce(int workers, TimeSpan duration, byte[] input, Size size)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var latencies = new List<double>[workers];
        var stopwatch = Stopwatch.StartNew();
        var threads = new Thread[workers];
        for (var w = 0; w < workers; w++)
        {
            var samples = latencies[w] = new List<double>(capacity: 4096);
            threads[w] = new Thread(() =>
            {
                using var output = new MemoryStream();
                while (stopwatch.Elapsed < duration)
                {
                    var start = Stopwatch.GetTimestamp();
                    ImageWorkloads.JpegDecodeResizeEncode(input, size, output);
                    samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            });
            threads[w].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        var elapsed = stopwatch.Elapsed;
        var all = latencies.SelectMany(list => list).Order().ToArray();
        var allocatedPerRequest = (GC.GetTotalAllocatedBytes(precise: true) - allocated) / Math.Max(1, all.Length);
        using var process = Process.GetCurrentProcess();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {workers} | {all.Length} | {all.Length / elapsed.TotalSeconds:0.0} | {Percentile(all, 0.50):0.00} | {Percentile(all, 0.95):0.00} | {Percentile(all, 0.99):0.00} | {all[^1]:0.00} | {allocatedPerRequest / 1024.0:0.0} KB | {GC.CollectionCount(0) - gen0} | {GC.CollectionCount(1) - gen1} | {GC.CollectionCount(2) - gen2} | {process.PeakWorkingSet64 / (1024.0 * 1024):0} MB |"));
    }

    private static double Percentile(double[] sorted, double fraction) => sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];

    private static int GetOption(string[] args, string name, int defaultValue)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? int.Parse(args[index + 1], CultureInfo.InvariantCulture) : defaultValue;
    }

    private static string RuntimeInformationText()
        => $".NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical CPUs, Server GC = {System.Runtime.GCSettings.IsServerGC}";
}
