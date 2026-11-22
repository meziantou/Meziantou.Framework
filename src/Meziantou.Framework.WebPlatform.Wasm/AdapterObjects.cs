using Meziantou.Framework.WebPlatform.WebAssembly;
using Engine = Meziantou.Framework.WasmEngine;

namespace Meziantou.Framework.WebPlatform.Wasm;

// The objects of the runtime abstraction of Meziantou.Framework.WebPlatform, implemented with the objects of
// Meziantou.Framework.WasmEngine. The exceptions of the engine are converted to the exceptions of the abstraction; the
// other exceptions (thrown by the host functions or by the host) are thrown as is.

internal sealed class AdapterModule(WasmWebAssemblyRuntime runtime, Engine.WasmModule value, byte[] bytes) : WasmModule(bytes)
{
    private IReadOnlyList<WasmImport>? _imports;
    private IReadOnlyList<WasmExport>? _exports;

    public Engine.WasmModule Value { get; } = value;

    public override WebAssemblyRuntime Runtime => runtime;

    public override IReadOnlyList<WasmImport> Imports => _imports ??= [.. Value.Imports.Select(import => new WasmImport(import.Module, import.Name, WasmWebAssemblyRuntime.FromEngine(import.Type)))];

    public override IReadOnlyList<WasmExport> Exports => _exports ??= [.. Value.Exports.Select(export => new WasmExport(export.Name, WasmWebAssemblyRuntime.FromEngine(export.Type)))];

    public override bool IsCharCodeArrayParameter(WasmFunctionType functionType, int parameterIndex)
        => functionType.Native is Engine.WasmFunctionType type && Value.IsMutableI16ArrayParameter(type, parameterIndex);
}

internal sealed class AdapterInstance(IReadOnlyList<(string Name, WasmExtern Value)> exports) : WasmInstance
{
    public override IReadOnlyList<(string Name, WasmExtern Value)> Exports { get; } = exports;
}

internal sealed class AdapterFunction(WasmWebAssemblyRuntime runtime, Engine.WasmFunction value) : WasmFunction
{
    private WasmFunctionType? _type;

    public WasmWebAssemblyRuntime Runtime { get; } = runtime;

    public Engine.WasmFunction Value { get; } = value;

    public override WasmFunctionType Type => _type ??= WasmWebAssemblyRuntime.FromEngine(Value.Type);

    public override void Invoke(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results)
    {
        var engineArguments = Runtime.ToEngine(arguments);
        var engineResults = results.IsEmpty ? [] : new Engine.WasmValue[results.Length];
        try
        {
            Value.Invoke(engineArguments, engineResults);
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }

        for (var i = 0; i < engineResults.Length; i++)
        {
            results[i] = Runtime.FromEngine(engineResults[i]);
        }
    }
}

internal sealed class AdapterMemory : WasmMemory
{
    public AdapterMemory(WasmWebAssemblyRuntime runtime, Engine.WasmMemory value)
    {
        Runtime = runtime;
        Value = value;
        Type = WasmWebAssemblyRuntime.FromEngine(value.Type);
        value.DataChanged += (_, _) => OnDataChanged();
    }

    public WasmWebAssemblyRuntime Runtime { get; }

    public Engine.WasmMemory Value { get; }

    public override WasmMemoryType Type { get; }

    public override byte[] Data => Value.Data;

    /// <summary>The bytes of a shared memory created by a store of the runtime (see <see cref="AdapterStore"/>).</summary>
    public override WasmSharedMemoryBytes? SharedBytes => (Value.SharedBlock as AdapterSharedMemoryBlock)?.Bytes;

    public override ulong Size => Value.Size;

    public override ulong Grow(ulong delta)
    {
        try
        {
            return Value.Grow(delta);
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }

    public override ulong GrowTo(ulong newSize)
    {
        try
        {
            return Value.GrowTo(newSize);
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }

    public override uint AtomicNotify(ulong address, uint count)
    {
        try
        {
            return Value.AtomicNotify(address, count);
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }

    public override WasmWaitResult AtomicWait(ulong address, long expected, int size, TimeSpan? timeout, WasmWaitSignal? signal = null)
    {
        Engine.WasmWaitSignal? engineSignal = null;
        if (signal is not null)
        {
            var created = new Engine.WasmWaitSignal();
            signal.Attach(() => created.IsSet);
            engineSignal = created;
        }

        try
        {
            return (WasmWaitResult)Value.AtomicWait(address, expected, size, timeout, engineSignal);
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }
}

/// <summary>The bytes of a shared memory: a shared data block of the script engine (see <see cref="IWasmHost.CreateSharedMemoryBytes"/>).</summary>
internal sealed class AdapterSharedMemoryBlock(WasmSharedMemoryBytes bytes) : Engine.WasmSharedMemoryBlock(bytes.Address, bytes.Capacity)
{
    public WasmSharedMemoryBytes Bytes { get; } = bytes;

    public override ulong ByteLength => Bytes.ByteLength;

    public override bool TryGrow(ulong newByteLength) => Bytes.TryGrow(newByteLength);
}

internal sealed class AdapterTable(WasmWebAssemblyRuntime runtime, Engine.WasmTable value) : WasmTable
{
    public WasmWebAssemblyRuntime Runtime { get; } = runtime;

    public Engine.WasmTable Value { get; } = value;

    public override WasmTableType Type { get; } = WasmWebAssemblyRuntime.FromEngine(value.Type);

    public override ulong Size => Value.Size;

    public override WasmValue Get(ulong index)
    {
        try
        {
            return Runtime.FromEngine(Value.Get(index));
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }

    public override void Set(ulong index, WasmValue value)
    {
        try
        {
            Value.Set(index, Runtime.ToEngine(value));
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }

    public override ulong Grow(ulong delta, WasmValue value)
    {
        try
        {
            return Value.Grow(delta, Runtime.ToEngine(value));
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }
}

internal sealed class AdapterGlobal(WasmWebAssemblyRuntime runtime, Engine.WasmGlobal value) : WasmGlobal
{
    public WasmWebAssemblyRuntime Runtime { get; } = runtime;

    public Engine.WasmGlobal Value { get; } = value;

    public override WasmGlobalType Type { get; } = WasmWebAssemblyRuntime.FromEngine(value.Type);

    public override WasmValue Get()
    {
        try
        {
            return Runtime.FromEngine(Value.Get());
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }

    public override void Set(WasmValue value)
    {
        try
        {
            Value.Set(Runtime.ToEngine(value));
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }
}

internal sealed class AdapterTag(WasmWebAssemblyRuntime runtime, Engine.WasmTag value) : WasmTag
{
    public WasmWebAssemblyRuntime Runtime { get; } = runtime;

    public Engine.WasmTag Value { get; } = value;

    public override WasmTagType Type { get; } = WasmWebAssemblyRuntime.FromEngine(value.Type);
}

internal sealed class AdapterGcObject(WasmWebAssemblyRuntime runtime, Engine.WasmGcObject value) : WasmGcObject
{
    public WasmWebAssemblyRuntime Runtime { get; } = runtime;

    public Engine.WasmGcObject Value { get; } = value;

    public override bool IsArray => Value.IsArray;
}

internal sealed class AdapterExceptionReference(WasmWebAssemblyRuntime runtime, Engine.WasmExceptionReference value) : WasmExceptionReference
{
    public WasmWebAssemblyRuntime Runtime { get; } = runtime;

    public Engine.WasmExceptionReference Value { get; } = value;

    public override WasmTag Tag => Runtime.GetTag(Value.Tag);

    public override int ArgumentCount => Value.ArgumentCount;

    public override WasmValue GetArgument(int index)
    {
        try
        {
            return Runtime.FromEngine(Value.GetArgument(index));
        }
        catch (Engine.WasmException ex)
        {
            throw Runtime.ConvertException(ex);
        }
    }
}

/// <summary>The store of an agent: a store of the engine, interrupted by the host, whose shared memories are shared data blocks of the host.</summary>
internal sealed class AdapterStore : WasmStore
{
    private readonly WasmWebAssemblyRuntime _runtime;
    private readonly Engine.WasmStore _store;
    private WasmTag? _javaScriptExceptionTag;

    public AdapterStore(WasmWebAssemblyRuntime runtime, IWasmHost host)
    {
        _runtime = runtime;
        _store = runtime.Runtime.CreateStore(new Engine.WasmStoreOptions
        {
            CheckInterrupt = host.CheckInterrupt,
            SharedMemoryAllocator = (byteLength, capacity) => new AdapterSharedMemoryBlock(host.CreateSharedMemoryBytes(byteLength, capacity)),
        });
    }

    public override WasmInstance Instantiate(WasmModule module, IReadOnlyList<WasmExtern> imports)
    {
        if (module is not AdapterModule adapterModule || adapterModule.Runtime != _runtime)
            throw new WasmLinkException("The module was not compiled by the runtime of the store");

        var engineImports = new Engine.WasmExtern[imports.Count];
        for (var i = 0; i < engineImports.Length; i++)
        {
            engineImports[i] = _runtime.ToEngine(imports[i]) ?? throw new WasmLinkException($"The import #{i} cannot be imported by this module");
        }

        Engine.WasmInstance instance;
        try
        {
            instance = _store.Instantiate(adapterModule.Value, engineImports);
        }
        catch (Engine.WasmException ex)
        {
            throw _runtime.ConvertException(ex);
        }
        catch (ArgumentException ex)
        {
            throw new WasmLinkException(ex.Message);
        }

        var exports = new (string Name, WasmExtern Value)[instance.Exports.Count];
        for (var i = 0; i < exports.Length; i++)
        {
            var (name, value) = instance.Exports[i];
            exports[i] = (name, _runtime.GetExtern(value));
        }

        return new AdapterInstance(exports);
    }

    public override WasmFunction CreateHostFunction(WasmFunctionType type, object? hostFunction, WasmHostCallback callback)
    {
        var runtime = _runtime;
        var function = Call(() => _store.CreateFunction(WasmWebAssemblyRuntime.ToEngine(type), (arguments, results) =>
        {
            var managedArguments = new WasmValue[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                managedArguments[i] = runtime.FromEngine(arguments[i]);
            }

            var managedResults = new WasmValue[results.Length];
            try
            {
                callback(managedArguments, managedResults);
            }
            catch (WasmThrowException ex) when (ex.Exception is AdapterExceptionReference exception && exception.Runtime == runtime)
            {
                // The exception is thrown in WebAssembly
                throw new Engine.WasmThrowException(exception.Value);
            }

            for (var i = 0; i < results.Length; i++)
            {
                results[i] = runtime.ToEngine(managedResults[i]);
            }
        }, hostFunction));

        return _runtime.GetFunction(function);
    }

    public override WasmMemory CreateMemory(WasmMemoryType type) => _runtime.GetMemory(Call(() => _store.CreateMemory(WasmWebAssemblyRuntime.ToEngine(type))));

    public override WasmTable CreateTable(WasmTableType type, WasmValue value) => _runtime.GetTable(Call(() => _store.CreateTable(WasmWebAssemblyRuntime.ToEngine(type), _runtime.ToEngine(value))));

    public override WasmGlobal CreateGlobal(WasmGlobalType type, WasmValue value) => _runtime.GetGlobal(Call(() => _store.CreateGlobal(WasmWebAssemblyRuntime.ToEngine(type), _runtime.ToEngine(value))));

    public override WasmTag CreateTag(WasmTagType type) => _runtime.GetTag(Call(() => _store.CreateTag(WasmWebAssemblyRuntime.ToEngine(type))));

    public override WasmExceptionReference CreateException(WasmTag tag, ReadOnlySpan<WasmValue> payload)
    {
        if (tag is not AdapterTag adapterTag || adapterTag.Runtime != _runtime)
            throw new WasmTypeMismatchException("The tag was not created by the runtime");

        var values = _runtime.ToEngine(payload);
        return _runtime.GetException(Call(() => _store.CreateException(adapterTag.Value, values)));
    }

    public override int GetArrayLength(WasmGcObject array) => Call(() => _store.GetArrayLength(GetArray(array)));

    public override WasmValue GetArrayElement(WasmGcObject array, int index) => _runtime.FromEngine(Call(() => _store.GetArrayElement(GetArray(array), index)));

    public override void SetArrayElement(WasmGcObject array, int index, WasmValue value, WasmValueType type)
    {
        var engineValue = _runtime.ToEngine(value);
        Call(() =>
        {
            _store.SetArrayElement(GetArray(array), index, engineValue, WasmWebAssemblyRuntime.ToEngine(type));
            return 0;
        });
    }

    private Engine.WasmGcObject GetArray(WasmGcObject array) => array is AdapterGcObject gcObject && gcObject.Runtime == _runtime ? gcObject.Value : throw new WasmTrapException("The value is not an array of the store");

    public override WasmTag JavaScriptExceptionTag => _javaScriptExceptionTag ??= CreateTag(new WasmTagType([WasmValueType.ExternRef]));

    public override void Dispose() => _store.Dispose();

    private T Call<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Engine.WasmException ex)
        {
            throw _runtime.ConvertException(ex);
        }
    }
}
