using System.Diagnostics;
using BenchmarkDotNet.Running;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.JavaScriptEngine.Benchmarks;

// Usage:
//   dotnet run -c Release -f net10.0 -- [BenchmarkDotNet arguments]   runs the benchmarks
//   dotnet run -c Release -f net10.0 -- quick [workload] [iterations]  times the workloads with both engines (a quick, noisy comparison)
//   dotnet run -c Release -f net10.0 -- profile <workload or file> <seconds>   runs a workload (a script that defines run()) in a loop (to attach a profiler)
//   dotnet run -c Release -f net10.0 -- dump <workload or file>         prints the bytecode of a workload
if (args is ["dump", var dumpWorkload])
{
    Console.WriteLine(Disassembler.Disassemble(Workloads.All.TryGetValue(dumpWorkload, out var dumpCode) ? dumpCode : File.ReadAllText(dumpWorkload)));
    return 0;
}

if (args is ["profile", "Parse" or "CreateRealm", var fixedCostSeconds])
{
    // The fixed costs: the parsing of a large script, or the creation of an engine and a realm
    var isParse = args[1] == "Parse";
    var fixedCostEnd = Stopwatch.GetTimestamp() + (long)(double.Parse(fixedCostSeconds, System.Globalization.CultureInfo.InvariantCulture) * Stopwatch.Frequency);
    var fixedCostCount = 0;
    var fixedCostAllocated = GC.GetAllocatedBytesForCurrentThread();
    var fixedCostStart = Stopwatch.GetTimestamp();
    var fixedCostCpu = Process.GetCurrentProcess().TotalProcessorTime;
    while (Stopwatch.GetTimestamp() < fixedCostEnd)
    {
        if (isParse)
        {
            JsPreparedScript.Parse(Workloads.LargeScript);
        }
        else
        {
            using var realmEngine = new JsEngine();
            realmEngine.CreateRealm().Execute("1 + 1");
        }

        fixedCostCount++;
    }

    Console.WriteLine($"{fixedCostCount} runs (mean {Stopwatch.GetElapsedTime(fixedCostStart).TotalMilliseconds / fixedCostCount:F3} ms, CPU {(Process.GetCurrentProcess().TotalProcessorTime - fixedCostCpu).TotalMilliseconds / fixedCostCount:F3} ms), {(GC.GetAllocatedBytesForCurrentThread() - fixedCostAllocated) / fixedCostCount} bytes/run");
    if (isParse)
    {
        // The stages: parsing, scope analysis, bytecode generation
        var stages = new double[3];
        for (var i = 0; i < 20; i++)
        {
            var source = new Meziantou.Framework.JavaScriptEngine.Text.SourceText(Workloads.LargeScript, url: null, 0, 0);
            var stageStart = Stopwatch.GetTimestamp();
            var program = Meziantou.Framework.JavaScriptEngine.Parsing.JsParser.ParseScript(source, Meziantou.Framework.JavaScriptEngine.Parsing.ParserOptions.Script);
            stages[0] += Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
            stageStart = Stopwatch.GetTimestamp();
            var analysis = Meziantou.Framework.JavaScriptEngine.Compiler.ScopeAnalyzer.Analyze(program, Meziantou.Framework.JavaScriptEngine.Compiler.ProgramKind.Script, isStrict: false);
            stages[1] += Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
            GC.KeepAlive(analysis);
            program = Meziantou.Framework.JavaScriptEngine.Parsing.JsParser.ParseScript(source, Meziantou.Framework.JavaScriptEngine.Parsing.ParserOptions.Script);
            stageStart = Stopwatch.GetTimestamp();
            Meziantou.Framework.JavaScriptEngine.Compiler.CodeGenerator.CompileProgram(program, source, Meziantou.Framework.JavaScriptEngine.Compiler.ProgramKind.Script, isStrict: false);
            stages[2] += Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds;
        }

        Console.WriteLine($"parse {stages[0] / 20:F3} ms, analyze {stages[1] / 20:F3} ms, generate {(stages[2] - stages[1]) / 20:F3} ms");
    }

    return 0;
}

if (args is ["profile", var workload, var seconds])
{
    using var engine = new JsEngine();
    var realm = engine.CreateRealm();
    realm.Execute(Workloads.All.TryGetValue(workload, out var profileCode) ? profileCode : File.ReadAllText(workload));
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
    var start = Stopwatch.GetTimestamp();
    var startCpu = Process.GetCurrentProcess().TotalProcessorTime;
    var best = double.MaxValue;
    while (Stopwatch.GetTimestamp() < end)
    {
        var runStart = Stopwatch.GetTimestamp();
        engine.Call(run, JsValue.Undefined);
        best = Math.Min(best, Stopwatch.GetElapsedTime(runStart).TotalMilliseconds);
        count++;
    }

    var mean = Stopwatch.GetElapsedTime(start).TotalMilliseconds / Math.Max(count, 1);
    var cpu = (Process.GetCurrentProcess().TotalProcessorTime - startCpu).TotalMilliseconds / Math.Max(count, 1);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Console.WriteLine($"{count} runs (mean {mean:F3} ms, best {best:F3} ms, CPU {cpu:F3} ms), {allocated / Math.Max(count, 1)} bytes/run, GC gen0 {GC.CollectionCount(0) - gcBefore.Item1} gen1 {GC.CollectionCount(1) - gcBefore.Item2} gen2 {GC.CollectionCount(2) - gcBefore.Item3}, pauses {(GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds:F0} ms");
    return 0;
}

if (args is ["quick", ..])
{
    var filter = args.Length > 1 ? args[1] : null;
    var iterations = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 20;
    // A file name instead of a workload name: the script of the file (it defines run())
    var workloads = filter is not null && File.Exists(filter) ? new Dictionary<string, string>(StringComparer.Ordinal) { [Path.GetFileNameWithoutExtension(filter)] = File.ReadAllText(filter) } : Workloads.All;
    if (!ReferenceEquals(workloads, Workloads.All))
    {
        filter = null;
    }

    foreach (var (name, code) in workloads)
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

    // The fixed costs: an engine and a realm that runs a tiny script, and the parsing of a large script
    if (ReferenceEquals(workloads, Workloads.All) && filter is null or "CreateRealm")
    {
        var mine = Measure(() => { using var engine = new JsEngine(); engine.CreateRealm().Execute("1 + 1"); }, iterations);
        var theirs = Measure(() => { using var engine = new Jint.Engine(); engine.Evaluate("1 + 1"); }, iterations);
        Console.WriteLine($"{"CreateRealm",-14} {mine,10:F3} ms  Jint {theirs,10:F3} ms  ratio {mine / theirs,6:F2}");
    }

    if (ReferenceEquals(workloads, Workloads.All) && filter is null or "Parse")
    {
        var mine = Measure(() => JsPreparedScript.Parse(Workloads.LargeScript), iterations);
        var theirs = Measure(() => Jint.Engine.PrepareScript(Workloads.LargeScript), iterations);
        Console.WriteLine($"{"Parse",-14} {mine,10:F3} ms  Jint {theirs,10:F3} ms  ratio {mine / theirs,6:F2}");
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
