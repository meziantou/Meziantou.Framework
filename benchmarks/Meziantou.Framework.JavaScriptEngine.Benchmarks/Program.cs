using System.Diagnostics;
using BenchmarkDotNet.Running;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.JavaScriptEngine.Benchmarks;

// Usage:
//   dotnet run -c Release -f net10.0 -- [BenchmarkDotNet arguments]   runs the benchmarks
//   dotnet run -c Release -f net10.0 -- quick [workload] [iterations]  times the workloads with both engines (a quick, noisy comparison)
//   dotnet run -c Release -f net10.0 -- profile <workload> <seconds>   runs a workload in a loop (to attach a profiler)
//   dotnet run -c Release -f net10.0 -- dump <workload or file>         prints the bytecode of a workload
if (args is ["dump", var dumpWorkload])
{
    Console.WriteLine(Disassembler.Disassemble(Workloads.All.TryGetValue(dumpWorkload, out var dumpCode) ? dumpCode : File.ReadAllText(dumpWorkload)));
    return 0;
}

if (args is ["profile", var workload, var seconds])
{
    using var engine = new JsEngine();
    var realm = engine.CreateRealm();
    realm.Execute(Workloads.All[workload]);
    JsValue run;
    using (engine.Enter())
    {
        run = realm.GlobalObject.GetProperty("run");
    }

    var end = Stopwatch.GetTimestamp() + (long)(double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture) * Stopwatch.Frequency);
    var count = 0;
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var gcBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    var pauseBefore = GC.GetTotalPauseDuration();
    while (Stopwatch.GetTimestamp() < end)
    {
        engine.Call(run, JsValue.Undefined);
        count++;
    }

    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Console.WriteLine($"{count} runs, {allocated / Math.Max(count, 1)} bytes/run, GC gen0 {GC.CollectionCount(0) - gcBefore.Item1} gen1 {GC.CollectionCount(1) - gcBefore.Item2} gen2 {GC.CollectionCount(2) - gcBefore.Item3}, pauses {(GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds:F0} ms");
    return 0;
}

if (args is ["quick", ..])
{
    var filter = args.Length > 1 ? args[1] : null;
    var iterations = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 20;
    foreach (var (name, code) in Workloads.All)
    {
        if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            continue;

        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute(code);
        JsValue run;
        using (engine.Enter())
        {
            run = realm.GlobalObject.GetProperty("run");
        }

        using var jint = new Jint.Engine();
        jint.Execute(code);
        var jintRun = jint.GetValue("run");
        var mine = Measure(() => engine.Call(run, JsValue.Undefined), iterations);
        var theirs = Measure(() => jint.Invoke(jintRun), iterations);
        Console.WriteLine($"{name,-14} {mine,10:F3} ms  Jint {theirs,10:F3} ms  ratio {mine / theirs,6:F2}");
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
