namespace Meziantou.Framework.WasmEngine.Benchmarks;

/// <summary>An instance of the module of the workloads (<see cref="Workloads"/>) in an engine.</summary>
internal abstract class EngineInstance : IDisposable
{
    public const string Interpreter = "WasmEngine-Interpreter";
    public const string Compiled = "WasmEngine-Compiled";

    public static IEnumerable<string> Names => [Interpreter, Compiled];

    public static EngineInstance Create(string name) => name switch
    {
        Interpreter => new ManagedEngineInstance(compilationThreshold: -1),
        Compiled => new ManagedEngineInstance(compilationThreshold: 0),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown engine"),
    };

    /// <summary>Calls the export <c>run_{workload}</c>.</summary>
    public abstract long Run(string workload, int size);

    /// <summary>Calls the export <c>identity</c>: the cost of a call from .NET.</summary>
    public abstract long Identity(int value);

    public abstract void Dispose();
}

internal sealed class ManagedEngineInstance : EngineInstance
{
    private readonly WasmRuntime _runtime;
    private readonly WasmStore _store;
    private readonly Dictionary<string, WasmFunction> _functions = new(StringComparer.Ordinal);
    private readonly WasmFunction _identity;
    private readonly WasmValue[] _arguments = new WasmValue[1];
    private readonly WasmValue[] _results = new WasmValue[1];

    public ManagedEngineInstance(int compilationThreshold)
    {
        _runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = compilationThreshold });
        _store = _runtime.CreateStore();
        var inc = _store.CreateFunction(
            new WasmFunctionType([WasmValueType.I64], [WasmValueType.I64]),
            (arguments, results) => results[0] = WasmValue.FromI64(arguments[0].I64 + 1));
        var library = _store.Instantiate(_runtime.Compile(Workloads.LibraryBinary));
        var instance = _store.Instantiate(_runtime.Compile(Workloads.Binary), new WasmImports().Define("env", "inc", inc).Define("lib", library));
        foreach (var workload in Workloads.All.Keys)
        {
            _functions[workload] = instance.GetFunction("run_" + workload)!;
        }

        _identity = instance.GetFunction("identity")!;
    }

    public override long Run(string workload, int size) => Call(_functions[workload], size);

    public override long Identity(int value) => Call(_identity, value);

    private long Call(WasmFunction function, int value)
    {
        _arguments[0] = WasmValue.FromI32(value);
        function.Invoke(_arguments, _results);
        return _results[0].I64;
    }

    public override void Dispose()
    {
        _store.Dispose();
        _runtime.Dispose();
    }
}
