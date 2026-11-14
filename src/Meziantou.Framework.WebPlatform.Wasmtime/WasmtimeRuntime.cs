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
/// the multiple memories, the SIMD and relaxed SIMD instructions), and the threads (the shared memories and the atomic
/// instructions).
/// </para>
/// <para>
/// The bytes of a shared memory are a shared data block of the script engine, allocated for the maximum size of the memory:
/// the agents (the windows and the workers) share the memory, and <c>Atomics.wait</c> and <c>Atomics.notify</c> use the
/// waiters of Wasmtime, so that they notify the <c>memory.atomic.wait32</c> waiters and are notified by
/// <c>memory.atomic.notify</c>.
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(ulong Minimum, ulong? Maximum, bool Is64, bool Shared), WasmModule> _memoryModules = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(ulong? Maximum, bool Is64), WasmModule> _atomicsModules = new();
    private int _disposed;

    /// <summary>Creates a runtime.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public WasmtimeRuntime(WasmtimeRuntimeOptions? options = null)
    {
        Options = options ?? new WasmtimeRuntimeOptions();
        _engine = new EngineHandle(CreateEngine(Options));

        // The epoch interruption: the running code calls the deadline callback of its store at each tick. The state does
        // not reference the runtime, so that a runtime that is not disposed can be collected (with its timer).
        _epochTimer = new Timer(static state =>
        {
            try
            {
                using var engine = new SafeHandleLease((EngineHandle)state!);
                NativeMethods.wasmtime_engine_increment_epoch(engine.Value);
            }
            catch (ObjectDisposedException)
            {
                // The runtime is disposed (Timer.Dispose does not wait for the running callbacks)
            }
        }, _engine, Options.InterruptionCheckInterval, Options.InterruptionCheckInterval);
    }

    /// <summary>Gets the options of the runtime.</summary>
    public WasmtimeRuntimeOptions Options { get; }

    /// <summary>Returns a lease on the native engine (<c>wasm_engine_t</c>), disposed after the native calls that use it.</summary>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    internal SafeHandleLease EnterEngine()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new SafeHandleLease(_engine);
    }

    private static unsafe nint CreateEngine(WasmtimeRuntimeOptions options)
    {
        var config = NativeMethods.wasm_config_new();
        NativeMethods.wasmtime_config_epoch_interruption_set(config, value: true);
        NativeMethods.wasmtime_config_max_wasm_stack_set(config, (nuint)options.MaxStackSize);

        // WebAssembly 3.0 (https://webassembly.org/features/) and the threads (the shared memories are created by the memory
        // creator too, see SharedLinearMemory)
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
        NativeMethods.wasmtime_config_wasm_threads_set(config, value: true);
        NativeMethods.wasmtime_config_shared_memory_set(config, value: true);

        // The traps are detected by explicit checks, not by signal handlers that would compete with the ones of .NET
        NativeMethods.wasmtime_config_signals_based_traps_set(config, value: false);

        // No native unwind information for the compiled code: Wasmtime would register it with the unwinder of the system
        // (__register_frame) when a module is created, and remove it when the module is deleted. On macOS, these calls take
        // the write lock of the FDE cache of libunwind, which every managed throw reads in cooperative mode (the runtime
        // unwinds one native frame with libunwind). A garbage collection that starts while threads wait for that lock can
        // hang the process: the collection waits for the waiting threads, whose waits are interrupted again and again by
        // its suspension signals, and they never get the lock (all the readers and the writers end up blocked on it).
        // The traps and the backtraces of Wasmtime walk the frame pointers: they do not need this information. Windows
        // requires it (Wasmtime panics when it is disabled there), and its unwinder has no such lock.
        if (!OperatingSystem.IsWindows())
        {
            NativeMethods.wasmtime_config_native_unwind_info_set(config, value: false);
        }

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
        using var engine = EnterEngine();
        fixed (byte* data = bytes)
        {
            var error = NativeMethods.wasmtime_module_validate(engine.Value, data, (nuint)bytes.Length);
            if (error == 0)
                return true;

            NativeMethods.wasmtime_error_delete(error);
            return false;
        }
    }

    internal override unsafe WasmModule Compile(byte[] bytes)
    {
        using var engine = EnterEngine();
        nint module;
        fixed (byte* data = bytes)
        {
            var error = NativeMethods.wasmtime_module_new(engine.Value, data, (nuint)bytes.Length, &module);
            if (error != 0)
                throw new WasmCompileException(NativeErrors.TakeErrorMessage(error));
        }

        return new WasmtimeModule(this, module, bytes);
    }

    internal override WasmStore CreateStore(IWasmHost host) => new WasmtimeStore(this, host);

    /// <summary>
    /// Whether a parameter of a native function type of the engine is <c>(ref null (array (mut i16)))</c>: the type is compared
    /// with the reference to an array type created by the engine, which is final, has no supertype and is in a recursion
    /// group of its own. The engine canonicalizes the types, so the array type of a module is the same type only when its
    /// definition is the same.
    /// </summary>
    internal unsafe bool IsCharCodeArrayParameter(NativeTypeHandle functionType, int parameterIndex)
    {
        using var engine = EnterEngine();
        using var type = new SafeHandleLease(functionType);
        var parameters = NativeMethods.wasm_functype_params(type.Value);
        if ((uint)parameterIndex >= (uint)parameters->Size)
            return false;

        var field = new WasmtimeFieldType { Mutable = 1, StorageKind = WasmtimeStorageTypeKind.I16 };
        var arrayType = NativeMethods.wasmtime_array_type_new(engine.Value, &field);
        try
        {
            var reference = new WasmtimeValType
            {
                Kind = WasmtimeValTypeKind.Ref,
                Nullable = 1,
                HeapType = new WasmtimeHeapType { Kind = (byte)WasmHeapKind.ConcreteArray, Concrete = arrayType },
            };

            // wasmtime_valtype_to_wasm copies the array type
            var expected = NativeMethods.wasmtime_valtype_to_wasm(engine.Value, &reference);
            try
            {
                return NativeMethods.wasmtime_wasm_valtype_equal(parameters->Data[parameterIndex], expected);
            }
            finally
            {
                NativeMethods.wasm_valtype_delete(expected);
            }
        }
        finally
        {
            NativeMethods.wasmtime_array_type_delete(arrayType);
        }
    }

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
    internal WasmModule GetMemoryModule(WasmMemoryType type) => _memoryModules.GetOrAdd((type.Minimum, type.Maximum, type.Is64, type.Shared), static (key, runtime) => runtime.Compile(WasmtimeModules.CreateMemoryModule(new WasmMemoryType(key.Minimum, key.Maximum, key.Is64, key.Shared))), this);

    /// <summary>
    /// Returns the wrapper of a shared memory, and takes the ownership of <paramref name="sharedMemory"/> (a
    /// <c>wasmtime_sharedmemory_t*</c>): the same wrapper for the same memory, whatever the store or the reference.
    /// </summary>
    internal unsafe WasmtimeSharedMemory GetSharedMemory(nint sharedMemory)
    {
        SharedMemoryHandle? handle = new(sharedMemory);
        try
        {
            if (SharedLinearMemory.Find((nint)NativeMethods.wasmtime_sharedmemory_data(sharedMemory)) is not { } linearMemory)
                throw new InvalidOperationException("The shared memory was not created by the runtime");

            lock (linearMemory)
            {
                if (linearMemory.Wrapper is { } wrapper && wrapper.TryGetTarget(out var existing))
                    return existing;

                var type = NativeMethods.wasmtime_sharedmemory_type(sharedMemory);
                WasmMemoryType memoryType;
                try
                {
                    memoryType = WasmtimeTypes.ReadMemoryType(type);
                }
                finally
                {
                    NativeMethods.wasm_memorytype_delete(type);
                }

                var memory = new WasmtimeSharedMemory(this, handle, linearMemory.Bytes, memoryType);
                handle = null;
                linearMemory.Wrapper = new WeakReference<WasmtimeSharedMemory>(memory);
                return memory;
            }
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <summary>
    /// <c>memory.atomic.notify</c> on a shared memory, from any thread: the instructions run in a store of their own (see
    /// <see cref="WasmtimeModules.CreateAtomicsModule"/>), so that they use the waiters of Wasmtime.
    /// </summary>
    internal uint AtomicNotify(WasmtimeSharedMemory memory, ulong address, uint count)
    {
        using var store = new WasmtimeStore(this, AtomicsHost.Instance);
        var notify = (WasmFunction)store.Instantiate(GetAtomicsModule(memory.Type), [memory]).Exports[0].Value;
        Span<WasmValue> results = [default];
        notify.Invoke([ToAddress(memory, address), WasmValue.FromI32(unchecked((int)count))], results);
        return unchecked((uint)results[0].I32);
    }

    /// <summary><c>memory.atomic.wait32</c> and <c>memory.atomic.wait64</c> on a shared memory, from any thread (see <see cref="AtomicNotify"/>): it blocks the calling thread.</summary>
    internal WasmWaitResult AtomicWait(WasmtimeSharedMemory memory, ulong address, long expected, int size, TimeSpan? timeout, Action? waiting)
    {
        // A negative timeout waits until notified
        var nanoseconds = timeout is { } duration ? (duration.Ticks >= long.MaxValue / 100 ? long.MaxValue : Math.Max(duration.Ticks, 0) * 100) : -1;
        using var store = new WasmtimeStore(this, AtomicsHost.Instance);
        var wait = (WasmFunction)store.Instantiate(GetAtomicsModule(memory.Type), [memory]).Exports[size == 8 ? 2 : 1].Value;
        Span<WasmValue> results = [default];
        waiting?.Invoke();
        wait.Invoke([ToAddress(memory, address), size == 8 ? WasmValue.FromI64(expected) : WasmValue.FromI32(unchecked((int)expected)), WasmValue.FromI64(nanoseconds)], results);
        return (WasmWaitResult)results[0].I32;
    }

    private static WasmValue ToAddress(WasmtimeSharedMemory memory, ulong address) => memory.Type.Is64 ? WasmValue.FromI64(unchecked((long)address)) : WasmValue.FromI32(unchecked((int)(uint)address));

    private WasmModule GetAtomicsModule(WasmMemoryType type) => _atomicsModules.GetOrAdd((type.Maximum, type.Is64), static (key, runtime) => runtime.Compile(WasmtimeModules.CreateAtomicsModule(key.Maximum, key.Is64)), this);

    /// <summary>The host of the stores of the atomic operations: they are not interrupted, and they create no memory.</summary>
    private sealed class AtomicsHost : IWasmHost
    {
        public static AtomicsHost Instance { get; } = new();

        public void CheckInterrupt()
        {
        }

        public WasmSharedMemoryBytes CreateSharedMemoryBytes(ulong byteLength, ulong capacity) => throw new WasmAllocationException("The store cannot create a shared memory");
    }

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
