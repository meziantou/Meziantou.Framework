using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Wasmtime.Interop;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>
/// The store of an agent (<c>wasmtime_store_t</c>). It is used by the thread of the agent.
/// </summary>
/// <remarks>
/// The native objects reference the managed objects by indices or weak handles (the data of the store, the environments
/// of the host functions, the host values of the <c>externref</c> values, the memories), so that a store that is not
/// disposed can be collected with the objects of its agent: the finalizer deletes the native store.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2216:Disposable types should declare finalizer", Justification = "The native store is released by its SafeHandle")]
internal sealed unsafe partial class WasmtimeStore : WasmStore
{
    private readonly WasmtimeRuntime _runtime;
    private readonly IWasmHost _host;
    private readonly StoreHandle _store;
    private nint _context;

    private readonly List<HostFunction> _hostFunctions = [];
    private readonly HashSet<HostValueSlot> _hostValues = [];
    private readonly Dictionary<nint, LinearMemory> _linearMemories = [];
    private readonly Dictionary<LinearMemory, WasmtimeMemory> _memoriesByLinearMemory = [];
    private readonly Dictionary<(ulong StoreId, nint Private), WasmtimeFunction> _functions = [];
    private readonly Dictionary<(ulong StoreId, uint Private1, uint Private2), WasmtimeMemory> _memories = [];
    private readonly Dictionary<(ulong StoreId, uint Private1, uint Private2), WasmtimeTable> _tables = [];
    private readonly Dictionary<(ulong StoreId, uint Private1, uint Private2), WasmtimeTag> _tags = [];
    private readonly Dictionary<(ulong StoreId, uint Private1, uint Private2, uint Private3), WasmtimeGlobal> _globals = [];
    private readonly Dictionary<uint, WeakReference<WasmtimeGcObject>> _gcObjects = [];
    private readonly Dictionary<uint, WeakReference<WasmtimeExceptionReference>> _exceptions = [];

    /// <summary>The exception thrown by a host function or by the host while WebAssembly runs, thrown again by the call into WebAssembly.</summary>
    private ExceptionDispatchInfo? _pendingException;
    private WasmtimeTag? _javaScriptExceptionTag;
    private WasmtimeFunction? _anyConvertExtern;
    private WasmtimeFunction? _externConvertAny;

    public WasmtimeStore(WasmtimeRuntime runtime, IWasmHost host)
    {
        _runtime = runtime;
        _host = host;
        using var engine = runtime.EnterEngine();
        var data = GCHandle.Alloc(this, GCHandleType.Weak);
        _store = new StoreHandle(NativeMethods.wasmtime_store_new(engine.Value, GCHandle.ToIntPtr(data), null), data);
        var store = _store.DangerousGetHandle();
        _context = NativeMethods.wasmtime_store_context(store);

        // The running code calls the callback at each tick of the epoch of the engine (see WasmtimeRuntime)
        NativeMethods.wasmtime_store_epoch_deadline_callback(store, &OnEpochDeadline, GCHandle.ToIntPtr(data), null);
        NativeMethods.wasmtime_context_set_epoch_deadline(_context, 1);
    }

    internal WasmtimeRuntime Runtime => _runtime;

    /// <summary>
    /// Returns a lease on the native store, disposed after the native calls that use it: its value is the context of the
    /// store, or of the caller of the running host function.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The store is disposed.</exception>
    internal SafeHandleLease EnterContext() => new(_store, _context);

    /// <summary>Releases the store. The native store is deleted when the running native calls return (see <see cref="SafeHandleLease"/>).</summary>
    public override void Dispose() => _store.Dispose();

    private static WasmtimeStore? FromContext(nint context)
    {
        var data = NativeMethods.wasmtime_context_get_data(context);
        return data == 0 ? null : GCHandle.FromIntPtr(data).Target as WasmtimeStore;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Linear memories
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Called by the memory creator for the memories created by this store.</summary>
    internal void AddLinearMemory(LinearMemory memory)
    {
        _linearMemories[memory.DataPointer] = memory;
        memory.Moved = OnLinearMemoryMoved;
    }

    private void OnLinearMemoryMoved(LinearMemory memory, nint previousPointer)
    {
        _linearMemories.Remove(previousPointer);
        _linearMemories[memory.DataPointer] = memory;
        if (_memoriesByLinearMemory.TryGetValue(memory, out var wrapper))
        {
            wrapper.RaiseDataChanged();
        }
    }

    /// <summary>Starts an operation that may create memories: the memory creator adds them to this store. Returns the previous store, given to <see cref="EndCreatingMemories"/>.</summary>
    private WasmtimeStore? BeginCreatingMemories()
    {
        var previous = LinearMemory.s_creatingStore;
        LinearMemory.s_creatingStore = this;
        return previous;
    }

    private static void EndCreatingMemories(WasmtimeStore? previous) => LinearMemory.s_creatingStore = previous;

    // ----------------------------------------------------------------------------------------------------------------
    // Calls and errors
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The callback of the epoch deadline: it lets the host interrupt the running code (the exception is thrown by the call
    /// into WebAssembly).
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint OnEpochDeadline(nint context, nint data, ulong* deadlineDelta, byte* updateKind)
    {
        *deadlineDelta = 1;
        *updateKind = 0; // WASMTIME_UPDATE_DEADLINE_CONTINUE
        if (GCHandle.FromIntPtr(data).Target is not WasmtimeStore store)
            return 0;

        try
        {
            store._host.CheckInterrupt();
            return 0;
        }
        catch (Exception ex)
        {
            store._pendingException = ExceptionDispatchInfo.Capture(ex);
            return NativeErrors.CreateError("The execution was interrupted");
        }
    }

    /// <summary>
    /// Starts a call into WebAssembly: the exception of the host of an enclosing call is saved. After the call, <see cref="ThrowIfFailed"/>
    /// throws the exception of its outcome, and <see cref="EndCall"/> restores the saved exception.
    /// </summary>
    private ExceptionDispatchInfo? BeginCall()
    {
        var previous = _pendingException;
        _pendingException = null;
        return previous;
    }

    private void EndCall(ExceptionDispatchInfo? previous) => _pendingException = previous;

    private void ThrowIfFailed(nint error, nint trap, Func<string, Exception> createErrorException)
    {
        if (error == 0 && trap == 0)
            return;

        try
        {
            // An exception of the host (e.g. the timeout of the script engine) is thrown as is
            if (_pendingException is { } pending)
            {
                _pendingException = null;
                pending.Throw();
            }

            if (trap != 0)
            {
                // An exception thrown by WebAssembly (https://webassembly.github.io/spec/js-api/#call-an-exported-function)
                using var context = EnterContext();
                WasmtimeRef exception;
                if (NativeMethods.wasmtime_context_take_exception(context.Value, &exception))
                    throw new WasmThrowException(GetException(&exception, owned: true));

                byte code;
                if (NativeMethods.wasmtime_trap_code(trap, &code) && code == 0) // WASMTIME_TRAP_CODE_STACK_OVERFLOW
                    throw new WasmTrapException("Maximum call stack size exceeded", WasmTrapKind.StackOverflow);

                throw new WasmTrapException(FirstLine(NativeErrors.GetTrapMessage(trap)));
            }

            var message = NativeErrors.TakeErrorMessage(error);
            error = 0;
            throw createErrorException(FirstLine(message));
        }
        finally
        {
            if (trap != 0)
            {
                NativeMethods.wasm_trap_delete(trap);
            }

            if (error != 0)
            {
                NativeMethods.wasmtime_error_delete(error);
            }
        }
    }

    /// <summary>The first line of a message of Wasmtime (the next lines are the backtrace).</summary>
    private static string FirstLine(string message)
    {
        var index = message.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? message : message[..index];
    }

    /// <summary>Throws the exception of an error of an operation that does not run WebAssembly code.</summary>
    private static void ThrowIfError(nint error, Func<string, Exception> createException)
    {
        if (error != 0)
            throw createException(FirstLine(NativeErrors.TakeErrorMessage(error)));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Instances
    // ----------------------------------------------------------------------------------------------------------------

    public override WasmInstance Instantiate(WasmModule module, IReadOnlyList<WasmExtern> imports)
    {
        var wasmtimeModule = (WasmtimeModule)module;
        var nativeImports = new WasmtimeExtern[imports.Count];
        for (var i = 0; i < nativeImports.Length; i++)
        {
            nativeImports[i] = imports[i] switch
            {
                WasmtimeFunction function when function.Store == this => new WasmtimeExtern { Kind = WasmtimeExternKind.Func, Func = function.Handle },
                WasmtimeGlobal global when global.Store == this => new WasmtimeExtern { Kind = WasmtimeExternKind.Global, Global = global.Handle },
                WasmtimeTable table when table.Store == this => new WasmtimeExtern { Kind = WasmtimeExternKind.Table, Table = table.Handle },
                WasmtimeMemory memory when memory.Store == this => new WasmtimeExtern { Kind = WasmtimeExternKind.Memory, Memory = memory.Handle },
                WasmtimeTag tag when tag.Store == this => new WasmtimeExtern { Kind = WasmtimeExternKind.Tag, Tag = tag.Handle },
                _ => throw new WasmLinkException($"The import #{i} cannot be imported by this module"),
            };
        }

        using var context = EnterContext();
        WasmtimeInstance instance = default;
        var previousStore = BeginCreatingMemories();
        var previousException = BeginCall();
        try
        {
            nint trap = 0;
            nint error;
            fixed (WasmtimeExtern* importsPointer = nativeImports)
            {
                error = NativeMethods.wasmtime_instance_new(context.Value, wasmtimeModule.Handle, importsPointer, (nuint)nativeImports.Length, &instance, &trap);
            }

            GC.KeepAlive(wasmtimeModule);
            ThrowIfFailed(error, trap, static message => new WasmLinkException(message));
        }
        finally
        {
            EndCall(previousException);
            EndCreatingMemories(previousStore);
        }

        // The exports, in the order of the module
        var exports = new List<(string Name, WasmExtern Value)>();
        var sections = module.Sections;
        for (nuint index = 0; ; index++)
        {
            byte* name;
            nuint nameLength;
            WasmtimeExtern item;
            if (!NativeMethods.wasmtime_instance_export_nth(context.Value, &instance, index, &name, &nameLength, &item))
                break;

            // A re-exported import is the same external value: Wasmtime returns another handle for it
            var exportName = System.Text.Encoding.UTF8.GetString(name, checked((int)nameLength));
            if (sections.GetReexportedImport(exportName) is { } importIndex && importIndex < imports.Count)
            {
                exports.Add((exportName, imports[importIndex]));
                continue;
            }

            exports.Add((exportName, GetExtern(&item, sections.GetExportedTable(exportName))));
        }

        return new WasmtimeModuleInstance(exports);
    }

    private WasmExtern GetExtern(WasmtimeExtern* item, WasmTableLimits? tableLimits) => item->Kind switch
    {
        WasmtimeExternKind.Func => GetFunction(item->Func),
        WasmtimeExternKind.Global => GetGlobal(item->Global),
        WasmtimeExternKind.Table => GetTable(item->Table, tableLimits),
        WasmtimeExternKind.Memory => GetMemory(item->Memory),
        WasmtimeExternKind.Tag => GetTag(item->Tag),
        _ => throw new NotSupportedException("The shared memories are not supported"),
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Functions
    // ----------------------------------------------------------------------------------------------------------------

    internal WasmtimeFunction GetFunction(WasmtimeFunc handle)
    {
        if (!_functions.TryGetValue((handle.StoreId, handle.Private), out var function))
        {
            function = new WasmtimeFunction(this, handle, hostFunction: null);
            _functions.Add((handle.StoreId, handle.Private), function);
        }

        return function;
    }

    internal WasmFunctionType GetFunctionType(WasmtimeFunc handle)
    {
        using var context = EnterContext();
        var type = NativeMethods.wasmtime_func_type(context.Value, &handle);
        try
        {
            return WasmtimeTypes.ReadFunctionType(type);
        }
        finally
        {
            NativeMethods.wasm_functype_delete(type);
        }
    }

    public override WasmFunction CreateHostFunction(WasmFunctionType type, object? hostFunction, WasmHostCallback callback)
    {
        using var engine = _runtime.EnterEngine();
        using var context = EnterContext();
        var nativeType = WasmtimeTypes.CreateFunctionType(engine.Value, type);
        try
        {
            // The environment is the index of the callback in the store
            var index = _hostFunctions.Count;
            _hostFunctions.Add(new HostFunction(type, callback));
            WasmtimeFunc handle;
            NativeMethods.wasmtime_func_new(context.Value, nativeType, &InvokeHostFunction, index, null, &handle);
            var function = new WasmtimeFunction(this, handle, hostFunction) { KnownType = type.Native is null ? null : type };
            _functions[(handle.StoreId, handle.Private)] = function;
            return function;
        }
        finally
        {
            NativeMethods.wasm_functype_delete(nativeType);
        }
    }

    internal void Invoke(WasmtimeFunction function, ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results)
    {
        var type = function.Type;
        var parameterTypes = type.Parameters;
        var resultTypes = type.Results;
        if (arguments.Length != parameterTypes.Length || results.Length != resultTypes.Length)
            throw new WasmTypeMismatchException("The number of arguments or results does not match the type of the function");

        var nativeArguments = new WasmtimeVal[parameterTypes.Length];
        var nativeResults = new WasmtimeVal[resultTypes.Length];
        var converted = 0;
        try
        {
            for (; converted < nativeArguments.Length; converted++)
            {
                nativeArguments[converted] = ToNative(arguments[converted], parameterTypes[converted]);
            }

            var handle = function.Handle;
            var previousException = BeginCall();
            try
            {
                nint trap = 0;
                nint error;
                using var context = EnterContext();
                fixed (WasmtimeVal* argumentsPointer = nativeArguments)
                fixed (WasmtimeVal* resultsPointer = nativeResults)
                {
                    error = NativeMethods.wasmtime_func_call(context.Value, &handle, argumentsPointer, (nuint)nativeArguments.Length, resultsPointer, (nuint)nativeResults.Length, &trap);
                }

                ThrowIfFailed(error, trap, static message => new WasmTypeMismatchException(message));
            }
            finally
            {
                EndCall(previousException);
            }
        }
        finally
        {
            for (var i = 0; i < converted; i++)
            {
                Unroot(ref nativeArguments[i]);
            }
        }

        // The results are owned by the caller
        for (var i = 0; i < nativeResults.Length; i++)
        {
            try
            {
                results[i] = FromNative(ref nativeResults[i]);
            }
            finally
            {
                Unroot(ref nativeResults[i]);
            }
        }
    }

    /// <summary><c>wasmtime_func_callback_t</c>: the environment is the index of the host function in its store.</summary>
    [UnmanagedCallersOnly]
    private static nint InvokeHostFunction(nint env, nint caller, WasmtimeVal* arguments, nuint argumentCount, WasmtimeVal* results, nuint resultCount)
    {
        var context = NativeMethods.wasmtime_caller_context(caller);
        if (FromContext(context) is not { } store)
            return NativeErrors.CreateTrap("The store was disposed");

        // The operations of the callback use the context of the caller
        var previousContext = store._context;
        store._context = context;
        try
        {
            return store.InvokeHostFunction(context, (int)env, arguments, (int)argumentCount, results, (int)resultCount);
        }
        finally
        {
            store._context = previousContext;
        }
    }

    private nint InvokeHostFunction(nint callerContext, int index, WasmtimeVal* arguments, int argumentCount, WasmtimeVal* results, int resultCount)
    {
        try
        {
            var hostFunction = _hostFunctions[index];
            var managedArguments = new WasmValue[argumentCount];
            for (var i = 0; i < argumentCount; i++)
            {
                // The arguments are borrowed: the references that are kept are cloned
                managedArguments[i] = FromNative(ref arguments[i]);
            }

            var managedResults = new WasmValue[resultCount];
            hostFunction.Callback(managedArguments, managedResults);
            var resultTypes = hostFunction.Type.Results;
            for (var i = 0; i < resultCount; i++)
            {
                // The results are owned by Wasmtime
                results[i] = ToNative(managedResults[i], resultTypes[i]);
            }

            return 0;
        }
        catch (WasmThrowException ex) when (ex.Exception is WasmtimeExceptionReference exception && exception.Store == this)
        {
            // The exception is thrown in WebAssembly (https://webassembly.github.io/spec/js-api/#create-a-host-function).
            // The context of the caller is valid even if the callback disposed the store: the call into WebAssembly holds
            // a lease on the store.
            using var reference = exception.EnterReference();
            return NativeMethods.wasmtime_context_set_exception(callerContext, (WasmtimeRef*)reference.Value);
        }
        catch (Exception ex)
        {
            // The exception cannot be caught by WebAssembly: it is thrown by the call into WebAssembly
            _pendingException = ExceptionDispatchInfo.Capture(ex);
            return NativeErrors.CreateTrap("The host function threw an exception");
        }
    }

    private sealed record HostFunction(WasmFunctionType Type, WasmHostCallback Callback);

    // ----------------------------------------------------------------------------------------------------------------
    // Memories
    // ----------------------------------------------------------------------------------------------------------------

    internal WasmtimeMemory GetMemory(WasmtimeIndexedExtern handle)
    {
        if (!_memories.TryGetValue((handle.StoreId, handle.Private1, handle.Private2), out var memory))
        {
            using var context = EnterContext();
            var data = (nint)NativeMethods.wasmtime_memory_data(context.Value, &handle);
            if (!_linearMemories.TryGetValue(data, out var linearMemory))
                throw new InvalidOperationException("The memory was not created by the store");

            var type = NativeMethods.wasmtime_memory_type(context.Value, &handle);
            try
            {
                memory = new WasmtimeMemory(this, handle, linearMemory, WasmtimeTypes.ReadMemoryType(type));
            }
            finally
            {
                NativeMethods.wasm_memorytype_delete(type);
            }

            _memories.Add((handle.StoreId, handle.Private1, handle.Private2), memory);
            _memoriesByLinearMemory[linearMemory] = memory;
        }

        return memory;
    }

    public override WasmMemory CreateMemory(WasmMemoryType type)
    {
        if (type.Shared)
            throw new WasmAllocationException("The shared memories are not supported");

        // The memories created by wasmtime_memory_new are not allocated by the memory creator: a module creates the memory
        var instance = Instantiate(_runtime.GetMemoryModule(type), []);
        return (WasmtimeMemory)instance.Exports[0].Value;
    }

    internal ulong GrowMemory(WasmtimeMemory memory, ulong delta)
    {
        var handle = memory.Handle;
        using var context = EnterContext();
        ulong previousSize;
        ThrowIfError(NativeMethods.wasmtime_memory_grow(context.Value, &handle, delta, &previousSize), static message => new WasmAllocationException(message));
        return previousSize;
    }

    internal ulong GetMemorySize(WasmtimeMemory memory)
    {
        var handle = memory.Handle;
        using var context = EnterContext();
        return NativeMethods.wasmtime_memory_size(context.Value, &handle);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Tables
    // ----------------------------------------------------------------------------------------------------------------

    internal WasmtimeTable GetTable(WasmtimeIndexedExtern handle, WasmTableLimits? limits)
    {
        if (!_tables.TryGetValue((handle.StoreId, handle.Private1, handle.Private2), out var table))
        {
            using var context = EnterContext();
            var type = NativeMethods.wasmtime_table_type(context.Value, &handle);
            try
            {
                table = new WasmtimeTable(this, handle, WasmtimeTypes.ReadTableType(type, limits));
            }
            finally
            {
                NativeMethods.wasm_tabletype_delete(type);
            }

            _tables.Add((handle.StoreId, handle.Private1, handle.Private2), table);
        }

        return table;
    }

    public override WasmTable CreateTable(WasmTableType type, WasmValue value)
    {
        // The tables of 64-bit addresses cannot be created by the C API: they are created by a module
        if (type.Is64 || type.Minimum > uint.MaxValue || type.Maximum > uint.MaxValue)
            return CreateTableFromModule(type, value);

        using var engine = _runtime.EnterEngine();
        using var context = EnterContext();
        var limits = new WasmLimits { Min = (uint)type.Minimum, Max = (uint)(type.Maximum ?? uint.MaxValue) };
        var nativeType = NativeMethods.wasm_tabletype_new(WasmtimeTypes.CreateValueType(engine.Value, type.ElementType), &limits);
        try
        {
            var init = ToNative(value, type.ElementType);
            try
            {
                WasmtimeIndexedExtern handle;
                ThrowIfError(NativeMethods.wasmtime_table_new(context.Value, nativeType, &init, &handle), static message => new WasmAllocationException(message));
                return GetTable(handle, new WasmTableLimits(type.Minimum, type.Maximum, Is64: false));
            }
            finally
            {
                Unroot(ref init);
            }
        }
        finally
        {
            NativeMethods.wasm_tabletype_delete(nativeType);
        }
    }

    private WasmtimeTable CreateTableFromModule(WasmTableType type, WasmValue value)
    {
        var module = (WasmtimeModule)_runtime.Compile(WasmtimeModules.CreateTableModule(type));
        var instance = Instantiate(module, []);
        var table = (WasmtimeTable)instance.Exports[0].Value;
        if (value.Reference is not null)
        {
            for (ulong i = 0; i < type.Minimum; i++)
            {
                table.Set(i, value);
            }
        }

        return table;
    }

    internal WasmValue GetTableElement(WasmtimeTable table, ulong index)
    {
        var handle = table.Handle;
        using var context = EnterContext();
        WasmtimeVal value;
        if (!NativeMethods.wasmtime_table_get(context.Value, &handle, index, &value))
            throw new WasmAllocationException("The index is out of the bounds of the table");

        try
        {
            return FromNative(ref value);
        }
        finally
        {
            Unroot(ref value);
        }
    }

    internal void SetTableElement(WasmtimeTable table, ulong index, WasmValue value)
    {
        var handle = table.Handle;
        using var context = EnterContext();
        if (index >= NativeMethods.wasmtime_table_size(context.Value, &handle))
            throw new WasmAllocationException("The index is out of the bounds of the table");

        var nativeValue = ToNative(value, table.Type.ElementType);
        try
        {
            ThrowIfError(NativeMethods.wasmtime_table_set(context.Value, &handle, index, &nativeValue), static message => new WasmTypeMismatchException(message));
        }
        finally
        {
            Unroot(ref nativeValue);
        }
    }

    internal ulong GetTableSize(WasmtimeTable table)
    {
        var handle = table.Handle;
        using var context = EnterContext();
        return NativeMethods.wasmtime_table_size(context.Value, &handle);
    }

    internal ulong GrowTable(WasmtimeTable table, ulong delta, WasmValue value)
    {
        var handle = table.Handle;
        using var context = EnterContext();
        var nativeValue = ToNative(value, table.Type.ElementType);
        try
        {
            ulong previousSize;
            ThrowIfError(NativeMethods.wasmtime_table_grow(context.Value, &handle, delta, &nativeValue, &previousSize), static message => new WasmAllocationException(message));
            return previousSize;
        }
        finally
        {
            Unroot(ref nativeValue);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Globals
    // ----------------------------------------------------------------------------------------------------------------

    internal WasmtimeGlobal GetGlobal(WasmtimeGlobalHandle handle)
    {
        if (!_globals.TryGetValue((handle.StoreId, handle.Private1, handle.Private2, handle.Private3), out var global))
        {
            using var context = EnterContext();
            var type = NativeMethods.wasmtime_global_type(context.Value, &handle);
            try
            {
                global = new WasmtimeGlobal(this, handle, WasmtimeTypes.ReadGlobalType(type));
            }
            finally
            {
                NativeMethods.wasm_globaltype_delete(type);
            }

            _globals.Add((handle.StoreId, handle.Private1, handle.Private2, handle.Private3), global);
        }

        return global;
    }

    public override WasmGlobal CreateGlobal(WasmGlobalType type, WasmValue value)
    {
        using var engine = _runtime.EnterEngine();
        using var context = EnterContext();
        var nativeType = WasmtimeTypes.CreateGlobalType(engine.Value, type);
        try
        {
            var nativeValue = ToNative(value, type.ValueType);
            try
            {
                WasmtimeGlobalHandle handle;
                ThrowIfError(NativeMethods.wasmtime_global_new(context.Value, nativeType, &nativeValue, &handle), static message => new WasmTypeMismatchException(message));
                return GetGlobal(handle);
            }
            finally
            {
                Unroot(ref nativeValue);
            }
        }
        finally
        {
            NativeMethods.wasm_globaltype_delete(nativeType);
        }
    }

    internal WasmValue GetGlobalValue(WasmtimeGlobal global)
    {
        var handle = global.Handle;
        using var context = EnterContext();
        WasmtimeVal value;
        NativeMethods.wasmtime_global_get(context.Value, &handle, &value);
        try
        {
            return FromNative(ref value);
        }
        finally
        {
            Unroot(ref value);
        }
    }

    internal void SetGlobalValue(WasmtimeGlobal global, WasmValue value)
    {
        var handle = global.Handle;
        using var context = EnterContext();
        var nativeValue = ToNative(value, global.Type.ValueType);
        try
        {
            ThrowIfError(NativeMethods.wasmtime_global_set(context.Value, &handle, &nativeValue), static message => new WasmTypeMismatchException(message));
        }
        finally
        {
            Unroot(ref nativeValue);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Tags and exceptions
    // ----------------------------------------------------------------------------------------------------------------

    internal WasmtimeTag GetTag(WasmtimeIndexedExtern handle)
    {
        if (!_tags.TryGetValue((handle.StoreId, handle.Private1, handle.Private2), out var tag))
        {
            using var context = EnterContext();
            var type = NativeMethods.wasmtime_tag_type(context.Value, &handle);
            try
            {
                tag = new WasmtimeTag(this, handle, WasmtimeTypes.ReadTagType(type));
            }
            finally
            {
                NativeMethods.wasm_tagtype_delete(type);
            }

            _tags.Add((handle.StoreId, handle.Private1, handle.Private2), tag);
        }

        return tag;
    }

    public override WasmTag CreateTag(WasmTagType type)
    {
        using var engine = _runtime.EnterEngine();
        using var context = EnterContext();
        var nativeType = WasmtimeTypes.CreateTagType(engine.Value, type);
        try
        {
            WasmtimeIndexedExtern handle;
            ThrowIfError(NativeMethods.wasmtime_tag_new(context.Value, nativeType, &handle), static message => new WasmTypeMismatchException(message));
            return GetTag(handle);
        }
        finally
        {
            NativeMethods.wasm_tagtype_delete(nativeType);
        }
    }

    public override WasmTag JavaScriptExceptionTag => _javaScriptExceptionTag ??= (WasmtimeTag)CreateTag(new WasmTagType([WasmValueType.ExternRef]));

    public override WasmExceptionReference CreateException(WasmTag tag, ReadOnlySpan<WasmValue> payload)
    {
        var wasmtimeTag = (WasmtimeTag)tag;
        var types = wasmtimeTag.Type.Parameters;
        if (payload.Length != types.Length)
            throw new WasmTypeMismatchException("The payload does not match the type of the tag");

        var fields = new WasmtimeVal[payload.Length];
        var converted = 0;
        try
        {
            for (; converted < fields.Length; converted++)
            {
                fields[converted] = ToNative(payload[converted], types[converted]);
            }

            var tagHandle = wasmtimeTag.Handle;
            using var context = EnterContext();
            WasmtimeRef exception;
            fixed (WasmtimeVal* fieldsPointer = fields)
            {
                ThrowIfError(NativeMethods.wasmtime_exnref_new(context.Value, &tagHandle, fieldsPointer, (nuint)fields.Length, &exception), static message => new WasmTypeMismatchException(message));
            }

            return GetException(&exception, owned: true);
        }
        finally
        {
            for (var i = 0; i < converted; i++)
            {
                Unroot(ref fields[i]);
            }
        }
    }

    /// <summary>Returns the object of an exception, the same object for the same exception. An owned reference is consumed.</summary>
    internal WasmtimeExceptionReference GetException(WasmtimeRef* reference, bool owned)
    {
        using var context = EnterContext();
        var raw = NativeMethods.wasmtime_exnref_to_raw(context.Value, reference);
        if (_exceptions.TryGetValue(raw, out var weak) && weak.TryGetTarget(out var existing))
        {
            if (owned)
            {
                NativeMethods.wasmtime_exnref_unroot(reference);
            }

            return existing;
        }

        WasmtimeRef rooted;
        if (owned)
        {
            rooted = *reference;
        }
        else
        {
            NativeMethods.wasmtime_exnref_clone(reference, &rooted);
        }

#pragma warning disable CA2000 // The handle is released when the object is collected
        var result = new WasmtimeExceptionReference(this, new RootedReferenceHandle(rooted, isException: true));
#pragma warning restore CA2000
        _exceptions[raw] = new WeakReference<WasmtimeExceptionReference>(result);
        return result;
    }

    internal WasmtimeTag GetExceptionTag(WasmtimeExceptionReference exception)
    {
        using var context = EnterContext();
        using var reference = exception.EnterReference();
        WasmtimeIndexedExtern tag;
        ThrowIfError(NativeMethods.wasmtime_exnref_tag(context.Value, (WasmtimeRef*)reference.Value, &tag), static message => new InvalidOperationException(message));
        return GetTag(tag);
    }

    internal int GetExceptionArgumentCount(WasmtimeExceptionReference exception)
    {
        using var context = EnterContext();
        using var reference = exception.EnterReference();
        return (int)NativeMethods.wasmtime_exnref_field_count(context.Value, (WasmtimeRef*)reference.Value);
    }

    internal WasmValue GetExceptionArgument(WasmtimeExceptionReference exception, int index)
    {
        using var context = EnterContext();
        using var reference = exception.EnterReference();
        WasmtimeVal value;
        ThrowIfError(NativeMethods.wasmtime_exnref_field(context.Value, (WasmtimeRef*)reference.Value, (nuint)index, &value), static message => new WasmAllocationException(message));
        try
        {
            return FromNative(ref value);
        }
        finally
        {
            Unroot(ref value);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // GC objects
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the object of a struct or of an array, the same object for the same struct or array. The reference is borrowed.</summary>
    private WasmtimeGcObject GetGcObject(WasmtimeRef* reference, bool isArray)
    {
        using var context = EnterContext();
        var raw = NativeMethods.wasmtime_anyref_to_raw(context.Value, reference);
        if (_gcObjects.TryGetValue(raw, out var weak) && weak.TryGetTarget(out var existing))
            return existing;

        WasmtimeRef rooted;
        NativeMethods.wasmtime_anyref_clone(reference, &rooted);
#pragma warning disable CA2000 // The handle is released when the object is collected
        var result = new WasmtimeGcObject(this, new RootedReferenceHandle(rooted, isException: false), isArray);
#pragma warning restore CA2000
        _gcObjects[raw] = new WeakReference<WasmtimeGcObject>(result);
        return result;
    }
}
