using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The activities and the metrics of the engine. Only the coarse operations are traced (the compilations and the
/// instantiations): the calls into WebAssembly and the host functions are not.
/// </summary>
internal sealed class WasmEngineTelemetry
{
    internal const string SourceName = "Meziantou.Framework.WasmEngine";
    internal const string MeterVersion = "1.0.0";

    internal const string CompileActivityName = "wasm.compile";
    internal const string InstantiateActivityName = "wasm.instantiate";

    internal const string CompileDurationName = "meziantou.framework.wasm_engine.module.compile.duration";
    internal const string ModuleSizeName = "meziantou.framework.wasm_engine.module.size";
    internal const string InstantiateDurationName = "meziantou.framework.wasm_engine.instance.instantiate.duration";
    internal const string TrapsName = "meziantou.framework.wasm_engine.traps.total";
    internal const string InterruptionsName = "meziantou.framework.wasm_engine.interruptions.total";
    internal const string MemoryUsageName = "meziantou.framework.wasm_engine.memory.usage";
    internal const string ActiveStoresName = "meziantou.framework.wasm_engine.stores.active";

    internal const string ModuleSizeTagName = "wasm.module.size";
    internal const string ModuleImportsTagName = "wasm.module.imports";
    internal const string ModuleExportsTagName = "wasm.module.exports";
    internal const string TrapKindTagName = "wasm.trap.kind";
    internal const string SharedMemoryTagName = "wasm.memory.shared";
    internal const string ErrorTypeTagName = "error.type";

    public static readonly ActivitySource ActivitySource = new(SourceName, MeterVersion);

    private static readonly ConditionalWeakTable<Meter, WasmEngineTelemetry> Instances = [];
    private static WasmEngineTelemetry? s_shared;

    private readonly Histogram<double> _compileDuration;
    private readonly Histogram<long> _moduleSize;
    private readonly Histogram<double> _instantiateDuration;
    private readonly Counter<long> _traps;
    private readonly Counter<long> _interruptions;
    private readonly UpDownCounter<long> _memoryUsage;
    private readonly UpDownCounter<long> _activeStores;

    private WasmEngineTelemetry(Meter meter)
    {
        _compileDuration = meter.CreateHistogram<double>(CompileDurationName, unit: "s", description: "Duration of the compilations of the modules.");
        _moduleSize = meter.CreateHistogram<long>(ModuleSizeName, unit: "By", description: "Size of the binaries of the compiled modules.");
        _instantiateDuration = meter.CreateHistogram<double>(InstantiateDurationName, unit: "s", description: "Duration of the instantiations of the modules (including their start function).");
        _traps = meter.CreateCounter<long>(TrapsName, unit: "{trap}", description: "Number of traps of the WebAssembly code.");
        _interruptions = meter.CreateCounter<long>(InterruptionsName, unit: "{interruption}", description: "Number of executions interrupted by the CheckInterrupt action of their store.");
        _memoryUsage = meter.CreateUpDownCounter<long>(MemoryUsageName, unit: "By", description: "Number of bytes allocated for the memories (the capacity of the shared memories).");
        _activeStores = meter.CreateUpDownCounter<long>(ActiveStoresName, unit: "{store}", description: "Number of stores that are not released.");
    }

    /// <summary>Returns the telemetry of the meter of a factory, or of the shared meter.</summary>
    public static WasmEngineTelemetry Get(IMeterFactory? meterFactory)
    {
        if (meterFactory is null)
            return LazyInitializer.EnsureInitialized(ref s_shared, () => new WasmEngineTelemetry(new Meter(SourceName, MeterVersion)));

        // The factory returns the same meter for the same name: its instruments are created once
        var meter = meterFactory.Create(new MeterOptions(SourceName) { Version = MeterVersion });
        return Instances.GetValue(meter, meter => new WasmEngineTelemetry(meter));
    }

    /// <summary>Starts the activity of a compilation, or returns null when nobody listens.</summary>
    public static Activity? StartCompile(int moduleSize)
    {
        if (!ActivitySource.HasListeners())
            return null;

        var activity = ActivitySource.StartActivity(CompileActivityName);
        activity?.SetTag(ModuleSizeTagName, moduleSize);
        return activity;
    }

    /// <summary>Starts the activity of an instantiation, or returns null when nobody listens.</summary>
    public static Activity? StartInstantiate(WasmModule module)
    {
        if (!ActivitySource.HasListeners())
            return null;

        var activity = ActivitySource.StartActivity(InstantiateActivityName);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(ModuleImportsTagName, module.Imports.Count);
            activity.SetTag(ModuleExportsTagName, module.Exports.Count);
        }

        return activity;
    }

    /// <summary>Sets the status of an activity of an operation that failed.</summary>
    public static void SetError(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetTag(ErrorTypeTagName, exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    public void RecordCompile(TimeSpan duration, int moduleSize, Exception? exception)
    {
        if (_compileDuration.Enabled)
        {
            _compileDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>(ErrorTypeTagName, exception?.GetType().FullName));
        }

        if (exception is null && _moduleSize.Enabled)
        {
            _moduleSize.Record(moduleSize);
        }
    }

    public void RecordInstantiate(TimeSpan duration, Exception? exception)
    {
        if (_instantiateDuration.Enabled)
        {
            _instantiateDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>(ErrorTypeTagName, exception?.GetType().FullName));
        }
    }

    /// <summary>Counts a trap once, whatever the number of calls it goes through.</summary>
    public void RecordTrap(WasmTrapException trap)
    {
        if (trap.IsRecorded)
            return;

        trap.IsRecorded = true;
        _traps.Add(1, new KeyValuePair<string, object?>(TrapKindTagName, trap.TrapKind is WasmTrapKind.StackOverflow ? "stack_overflow" : "other"));
    }

    public void RecordInterruption() => _interruptions.Add(1);

    public void RecordMemoryUsage(long delta, bool shared)
    {
        if (delta != 0)
        {
            _memoryUsage.Add(delta, new KeyValuePair<string, object?>(SharedMemoryTagName, shared));
        }
    }

    public void RecordStoreCreated() => _activeStores.Add(1);

    public void RecordStoreReleased() => _activeStores.Add(-1);
}
