using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.WasmEngine.Benchmarks;

/// <summary>Runs the workloads in each engine.</summary>
[MemoryDiagnoser]
public class EngineBenchmarks
{
    private EngineInstance? _instance;
    private int _size;

    [ParamsSource(nameof(WorkloadNames))]
    public string Workload { get; set; } = "";

    [ParamsSource(nameof(EngineNames))]
    public string Engine { get; set; } = "";

    public static IEnumerable<string> WorkloadNames => Workloads.All.Keys;

    public static IEnumerable<string> EngineNames => EngineInstance.Names;

    [GlobalSetup]
    public void Setup()
    {
        _instance = EngineInstance.Create(Engine);
        _size = Workloads.All[Workload];
    }

    [GlobalCleanup]
    public void Cleanup() => _instance?.Dispose();

    [Benchmark]
    public long Run() => _instance!.Run(Workload, _size);
}

/// <summary>Calls a function that returns its argument from .NET: the cost of a call.</summary>
[MemoryDiagnoser]
public class CallBenchmarks
{
    private const int Calls = 10_000;
    private EngineInstance? _instance;

    [ParamsSource(nameof(EngineNames))]
    public string Engine { get; set; } = "";

    public static IEnumerable<string> EngineNames => EngineInstance.Names;

    [GlobalSetup]
    public void Setup() => _instance = EngineInstance.Create(Engine);

    [GlobalCleanup]
    public void Cleanup() => _instance?.Dispose();

    [Benchmark(OperationsPerInvoke = Calls)]
    public long Call()
    {
        var instance = _instance!;
        var sum = 0L;
        for (var i = 0; i < Calls; i++)
        {
            sum += instance.Identity(i);
        }

        return sum;
    }
}

/// <summary>Compiles the module of the workloads, and instantiates it (with the module it imports).</summary>
[MemoryDiagnoser]
public class CompileBenchmarks
{
    private WasmRuntime? _runtime;
    private WasmModule? _module;
    private WasmModule? _library;

    [GlobalSetup]
    public void Setup()
    {
        _runtime = new WasmRuntime();
        _module = _runtime.Compile(Workloads.Binary);
        _library = _runtime.Compile(Workloads.LibraryBinary);
    }

    [GlobalCleanup]
    public void Cleanup() => _runtime?.Dispose();

    [Benchmark]
    public WasmModule Compile() => _runtime!.Compile(Workloads.Binary);

    [Benchmark]
    public void Instantiate()
    {
        using var store = _runtime!.CreateStore();
        var inc = store.CreateFunction(
            new WasmFunctionType([WasmValueType.I64], [WasmValueType.I64]),
            (arguments, results) => results[0] = WasmValue.FromI64(arguments[0].I64 + 1));
        var library = store.Instantiate(_library!);
        store.Instantiate(_module!, [inc, library.GetFunction("inc")!]);
    }
}
