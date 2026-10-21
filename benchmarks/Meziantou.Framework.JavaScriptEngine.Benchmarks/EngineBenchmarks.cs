using BenchmarkDotNet.Attributes;
using Jint;

namespace Meziantou.Framework.JavaScriptEngine.Benchmarks;

/// <summary>Runs the workloads with Meziantou.Framework.JavaScriptEngine and with Jint (the engine it replaces).</summary>
[MemoryDiagnoser]
public class EngineBenchmarks
{
    private JsEngine? _engine;
    private JsValue _run;
    private global::Jint.Engine? _jint;
    private global::Jint.Native.JsValue? _jintRun;

    [ParamsSource(nameof(WorkloadNames))]
    public string Workload { get; set; } = "";

    [Params("Meziantou", "Jint")]
    public string Engine { get; set; } = "";

    public static IEnumerable<string> WorkloadNames => Workloads.All.Keys;

    [GlobalSetup]
    public void Setup()
    {
        var code = Workloads.All[Workload];
        if (Engine == "Jint")
        {
            _jint = new global::Jint.Engine();
            _jint.Execute(code);
            _jintRun = _jint.GetValue("run");
        }
        else
        {
            _engine = new JsEngine();
            var realm = _engine.CreateRealm();
            realm.Execute(code);
            using var scope = _engine.Enter();
            _run = realm.GlobalObject.GetProperty("run");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
        _jint?.Dispose();
    }

    [Benchmark]
    public object Run() => Engine == "Jint" ? _jint!.Invoke(_jintRun!) : _engine!.Call(_run, JsValue.Undefined);
}

/// <summary>Parses and compiles a large script.</summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    [Benchmark]
    public JsPreparedScript Meziantou() => JsPreparedScript.Parse(Workloads.LargeScript);

    [Benchmark(Baseline = true)]
    public object Jint() => global::Jint.Engine.PrepareScript(Workloads.LargeScript);

    /// <summary>Creates an engine and a realm, and runs a tiny script.</summary>
    [Benchmark]
    public double CreateRealm()
    {
        using var engine = new JsEngine();
        return engine.CreateRealm().Execute("1 + 1").AsNumber();
    }

    [Benchmark]
    public double CreateJintEngine()
    {
        using var engine = new global::Jint.Engine();
        return engine.Evaluate("1 + 1").AsNumber();
    }
}
