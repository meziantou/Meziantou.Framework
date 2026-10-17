using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The <c>profile</c> command: runs one benchmark method in a loop for a fixed duration, without BenchmarkDotNet, so that a
/// profiler (for example <c>dotnet-trace collect -- dotnet ImagingBenchmarks.dll profile
/// PngBenchmarks.SaveLarge8 20</c>) sees only the workload. Usage: <c>profile &lt;Class.Method&gt; [seconds|iterations
/// followed by x]</c>. It prints the mean, minimum and median time of an iteration, the managed
/// bytes allocated per iteration and the activity of the shared buffer pool.
/// </summary>
internal static class ProfileLoop
{
    public static void Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: profile <Class.Method> [seconds|<iterations>x]");
            return;
        }

        var name = args[0].Split('.');
        var type = typeof(ProfileLoop).Assembly.GetTypes().Single(t => t.Name == name[0]);
        var method = type.GetMethod(name[1], BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException($"Unknown method {args[0]}.", nameof(args));

        // "<n>x" runs a fixed number of iterations instead of a duration: the work is then the same for two builds, which
        // lets a process-level counter (for example the retired instructions of "/usr/bin/time -l") compare them
        var fixedIterations = args.Length > 1 && args[1] is [.., 'x'] ? int.Parse(args[1][..^1], CultureInfo.InvariantCulture) : (int?)null;
        var duration = TimeSpan.FromSeconds(args.Length > 1 && fixedIterations is null ? double.Parse(args[1], CultureInfo.InvariantCulture) : 10);
        var instance = Activator.CreateInstance(type);
        var setup = type.GetMethods().SingleOrDefault(m => m.GetCustomAttribute<GlobalSetupAttribute>() is not null);
        var cleanup = type.GetMethods().SingleOrDefault(m => m.GetCustomAttribute<GlobalCleanupAttribute>() is not null);
        setup?.Invoke(instance, null);
        try
        {
            // The minimum and the median are reported with the mean: on a busy machine the mean mostly measures the other
            // processes, the minimum is the closest to the cost of the workload
            var times = new List<double>(fixedIterations ?? 4096);
            var pool = SlabPool.Shared.GetDiagnostics();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            while (fixedIterations is { } count ? times.Count < count : stopwatch.Elapsed < duration)
            {
                var start = Stopwatch.GetTimestamp();
                method.Invoke(instance, null);
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            times.Sort();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{args[0]}: {times.Count} iterations, {elapsed / times.Count:0.000} ms/op (min {times[0]:0.000}, median {times[times.Count / 2]:0.000}), {allocated / times.Count / 1024.0:0.0} KB allocated/op"));

            // What the buffer pool did for one iteration: rentals served from retained buffers, new buffers, and buffers
            // too large to be pooled
            var after = SlabPool.Shared.GetDiagnostics();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"pool per op: {(after.ReusedRentals - pool.ReusedRentals) / (double)times.Count:0.0} reused, {(after.NewPooledAllocations - pool.NewPooledAllocations) / (double)times.Count:0.0} new, {(after.UnpooledAllocations - pool.UnpooledAllocations) / (double)times.Count:0.0} unpooled, {(after.DroppedReturns - pool.DroppedReturns) / (double)times.Count:0.0} dropped; retained {after.RetainedBytes / 1024.0 / 1024.0:0.00} MB in {after.RetainedBuffers} buffers"));
        }
        finally
        {
            cleanup?.Invoke(instance, null);
            (instance as IDisposable)?.Dispose();
        }
    }
}
