using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.JavaScriptEngine.Benchmarks;

/// <summary>Runs the workloads.</summary>
[MemoryDiagnoser]
public class EngineBenchmarks
{
    private JsEngine? _engine;
    private JsValue _run;

    [ParamsSource(nameof(WorkloadNames))]
    public string Workload { get; set; } = "";

    public static IEnumerable<string> WorkloadNames => Workloads.All.Keys;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new JsEngine();
        var realm = _engine.CreateRealm();
        realm.Execute(Workloads.All[Workload]);
        using var scope = _engine.Enter();
        _run = realm.GlobalObject.GetProperty("run");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
    }

    [Benchmark]
    public JsValue Run() => _engine!.Call(_run, JsValue.Undefined);
}

/// <summary>Parses and compiles a large script.</summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    [Benchmark]
    public JsPreparedScript Parse() => JsPreparedScript.Parse(Workloads.LargeScript);

    /// <summary>Creates an engine and a realm, and runs a tiny script.</summary>
    [Benchmark]
    public double CreateRealm()
    {
        using var engine = new JsEngine();
        return engine.CreateRealm().Execute("1 + 1").AsNumber();
    }
}
