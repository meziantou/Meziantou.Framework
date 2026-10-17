using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Wasmtime.Interop;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>
/// A <see cref="WebAssemblyRuntime"/> based on Wasmtime (https://wasmtime.dev/): the WebAssembly modules are compiled to
/// native code by Cranelift. Set it in <see cref="Dom.BrowsingContextOptions.WebAssemblyRuntime"/> to define the
/// <c>WebAssembly</c> namespace in the realms of the scripts.
/// </summary>
/// <remarks>
/// <para>
/// The WebAssembly 3.0 features are enabled (the reference types, the GC types, the exceptions, the tail calls, memory64,
/// the multiple memories, the SIMD and relaxed SIMD instructions). The threads are not supported: the shared memories can
/// only be created by JavaScript and cannot be imported by a module.
/// </para>
/// <para>
/// The memories are .NET arrays, which the <c>ArrayBuffer</c> objects of JavaScript share. The WebAssembly code is
/// interrupted when the script engine interrupts the scripts (e.g. its timeout).
/// </para>
/// </remarks>
public sealed class WasmtimeRuntime : WebAssemblyRuntime, IDisposable
{
    private readonly EngineHandle _engine;
    private readonly Timer _epochTimer;
    private WasmModule? _conversionsModule;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(ulong Minimum, ulong? Maximum, bool Is64), WasmModule> _memoryModules = new();
    private int _disposed;

    /// <summary>Creates a runtime.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public WasmtimeRuntime(WasmtimeRuntimeOptions? options = null)
    {
        Options = options ?? new WasmtimeRuntimeOptions();
        _engine = new EngineHandle(CreateEngine(Options));

        // The epoch interruption: the running code calls the deadline callback of its store at each tick
        var weakSelf = new WeakReference<WasmtimeRuntime>(this);
        _epochTimer = new Timer(static state =>
        {
            var (weak, engine) = ((WeakReference<WasmtimeRuntime>, nint))state!;
            if (weak.TryGetTarget(out var runtime) && Volatile.Read(ref runtime._disposed) == 0)
            {
                NativeMethods.wasmtime_engine_increment_epoch(engine);
            }
        }, (weakSelf, _engine.DangerousGetHandle()), Options.InterruptionCheckInterval, Options.InterruptionCheckInterval);
    }

    /// <summary>Gets the options of the runtime.</summary>
    public WasmtimeRuntimeOptions Options { get; }

    internal nint Engine
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _engine.DangerousGetHandle();
        }
    }

    private static unsafe nint CreateEngine(WasmtimeRuntimeOptions options)
    {
        var config = NativeMethods.wasm_config_new();
        NativeMethods.wasmtime_config_epoch_interruption_set(config, value: true);
        NativeMethods.wasmtime_config_max_wasm_stack_set(config, (nuint)options.MaxStackSize);

        // WebAssembly 3.0 (https://webassembly.org/features/), without the threads (the shared memories are not .NET arrays)
        NativeMethods.wasmtime_config_wasm_bulk_memory_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_multi_value_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_reference_types_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_simd_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_relaxed_simd_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_relaxed_simd_deterministic_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_tail_call_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_function_references_set(config, value: true);
        NativeMethods.wasmtime_config_gc_support_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_gc_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_exceptions_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_multi_memory_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_memory64_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_wide_arithmetic_set(config, value: true);
        NativeMethods.wasmtime_config_wasm_threads_set(config, value: false);
        NativeMethods.wasmtime_config_shared_memory_set(config, value: false);

        // The traps are detected by explicit checks, not by signal handlers that would compete with the ones of .NET
        NativeMethods.wasmtime_config_signals_based_traps_set(config, value: false);

        // No native unwind information for the compiled code: Wasmtime would register it with the unwinder of the system
        // (__register_frame) when a module is created, and remove it when the module is deleted. On macOS, these calls take
        // the write lock of the FDE cache of libunwind, which every managed throw reads in cooperative mode (the runtime
        // unwinds one native frame with libunwind). A garbage collection that starts while threads wait for that lock can
        // hang the process: the collection waits for the waiting threads, whose waits are interrupted again and again by
        // its suspension signals, and they never get the lock (all the readers and the writers end up blocked on it).
        // The traps and the backtraces of Wasmtime walk the frame pointers: they do not need this information.
        NativeMethods.wasmtime_config_native_unwind_info_set(config, value: false);

        // The memories are .NET arrays (see LinearMemory): they are allocated with their exact size, without guard pages,
        // and move when they grow
        NativeMethods.wasmtime_config_memory_reservation_set(config, 0);
        NativeMethods.wasmtime_config_memory_guard_size_set(config, 0);
        NativeMethods.wasmtime_config_memory_reservation_for_growth_set(config, 0);
        NativeMethods.wasmtime_config_memory_may_move_set(config, value: true);
        NativeMethods.wasmtime_config_memory_init_cow_set(config, value: false);
        var creator = new WasmtimeMemoryCreator
        {
            // Freed by the finalizer of the creator, when the engine is deleted (after the runtime, the modules and the stores)
            Env = GCHandle.ToIntPtr(GCHandle.Alloc(new LinearMemoryLimits(options.MaxMemorySize))),
            NewMemory = &LinearMemory.NewMemory,
            Finalizer = &LinearMemory.FreeLimits,
        };
        NativeMethods.wasmtime_config_host_memory_creator_set(config, &creator);
        return NativeMethods.wasm_engine_new_with_config(config);
    }

    internal override unsafe bool Validate(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* data = bytes)
        {
            var error = NativeMethods.wasmtime_module_validate(Engine, data, (nuint)bytes.Length);
            if (error == 0)
                return true;

            NativeMethods.wasmtime_error_delete(error);
            return false;
        }
    }

    internal override unsafe WasmModule Compile(byte[] bytes)
    {
        nint module;
        fixed (byte* data = bytes)
        {
            var error = NativeMethods.wasmtime_module_new(Engine, data, (nuint)bytes.Length, &module);
            if (error != 0)
                throw new WasmCompileException(NativeErrors.TakeErrorMessage(error));
        }

        return new WasmtimeModule(this, module, bytes);
    }

    internal override WasmStore CreateStore(IWasmHost host) => new WasmtimeStore(this, host);

    /// <summary>Converts the text format of a module to its binary format (https://webassembly.github.io/spec/core/text/index.html).</summary>
    /// <exception cref="FormatException">The text is not valid.</exception>
    internal static unsafe byte[] WatToWasm(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        WasmByteVec result;
        fixed (byte* data = bytes)
        {
            var error = NativeMethods.wasmtime_wat2wasm(data, (nuint)bytes.Length, &result);
            if (error != 0)
                throw new FormatException(NativeErrors.TakeErrorMessage(error));
        }

        try
        {
            return result.AsSpan().ToArray();
        }
        finally
        {
            NativeMethods.wasm_byte_vec_delete(&result);
        }
    }

    /// <summary>Returns a module that exports a memory of a type (see <see cref="WasmtimeModules.CreateMemoryModule"/>), compiled once per type.</summary>
    internal WasmModule GetMemoryModule(WasmMemoryType type) => _memoryModules.GetOrAdd((type.Minimum, type.Maximum, type.Is64), static (key, runtime) => runtime.Compile(WasmtimeModules.CreateMemoryModule(new WasmMemoryType(key.Minimum, key.Maximum, key.Is64, shared: false))), this);

    /// <summary>The module of the conversions between the any and the extern hierarchies (see <see cref="WasmtimeModules.Conversions"/>).</summary>
    internal WasmModule ConversionsModule => LazyInitializer.EnsureInitialized(ref _conversionsModule, () => Compile(WasmtimeModules.Conversions));

    /// <summary>Releases the engine. The stores and the modules must not be used afterwards.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _epochTimer.Dispose();

        // The modules and the stores keep a reference to the engine of Wasmtime: it is deleted with the last of them
        _engine.Dispose();
    }
}
