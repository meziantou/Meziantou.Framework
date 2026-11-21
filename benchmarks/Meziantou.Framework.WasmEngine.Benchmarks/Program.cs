using System.Diagnostics;
using BenchmarkDotNet.Running;
using Meziantou.Framework.WasmEngine.Benchmarks;

// Usage:
//   dotnet run -c Release -f net10.0 -- --filter * --inProcess        runs the benchmarks (BenchmarkDotNet arguments)
//   dotnet run -c Release -f net10.0 -- quick [workload] [iterations]  times the workloads in each engine (a quick, noisy measurement)
if (args is ["quick", ..])
{
    var filter = args.Length > 1 ? args[1] : null;
    var iterations = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 10;
    Console.WriteLine($"{"Workload",-12} {"Engine",-24} {"Best (ms)",10} {"Result",20}");
    foreach (var (workload, size) in Workloads.All)
    {
        if (filter is not null && !workload.Contains(filter, StringComparison.OrdinalIgnoreCase))
            continue;

        foreach (var engine in EngineInstance.Names)
        {
            using var instance = EngineInstance.Create(engine);
            var result = 0L;
            var best = Measure(() => result = instance.Run(workload, size), iterations);
            Console.WriteLine($"{workload,-12} {engine,-24} {best,10:F3} {result,20}");
        }
    }

    return 0;
}

BenchmarkSwitcher.FromAssembly(typeof(EngineBenchmarks).Assembly).Run(args);
return 0;

static double Measure(Action action, int iterations)
{
    for (var i = 0; i < 3; i++)
    {
        action();
    }

    var best = double.MaxValue;
    for (var i = 0; i < iterations; i++)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    return best;
}
