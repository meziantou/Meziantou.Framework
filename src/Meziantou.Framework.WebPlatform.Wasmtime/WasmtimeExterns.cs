using Meziantou.Framework.WebPlatform.Wasmtime.Interop;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

internal sealed class WasmtimeModuleInstance(IReadOnlyList<(string Name, WasmExtern Value)> exports) : WasmInstance
{
    public override IReadOnlyList<(string Name, WasmExtern Value)> Exports { get; } = exports;
}

internal sealed class WasmtimeFunction : WasmFunction
{
    private WasmFunctionType? _type;

    public WasmtimeFunction(WasmtimeStore store, WasmtimeFunc handle, object? hostFunction)
    {
        Store = store;
        Handle = handle;
        HostFunction = hostFunction;
    }

    public WasmtimeStore Store { get; }

    public WasmtimeFunc Handle { get; }

    /// <summary>The type given when the function was created, which keeps the concrete heap types, or null.</summary>
    public WasmFunctionType? KnownType { get; init; }

    public override WasmFunctionType Type => _type ??= KnownType ?? Store.GetFunctionType(Handle);

    public override void Invoke(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results) => Store.Invoke(this, arguments, results);
}

internal sealed class WasmtimeMemory : WasmMemory
{
    private readonly LinearMemory _linearMemory;

    public WasmtimeMemory(WasmtimeStore store, WasmtimeIndexedExtern handle, LinearMemory linearMemory, WasmMemoryType type)
    {
        Store = store;
        Handle = handle;
        _linearMemory = linearMemory;
        Type = type;
    }

    public WasmtimeStore Store { get; }

    public WasmtimeIndexedExtern Handle { get; }

    public override WasmMemoryType Type { get; }

    public override byte[] Data => _linearMemory.Data;

    public override ulong Size => Store.GetMemorySize(this);

    public override ulong Grow(ulong delta) => Store.GrowMemory(this, delta);

    internal void RaiseDataChanged() => OnDataChanged();
}

internal sealed class WasmtimeTable(WasmtimeStore store, WasmtimeIndexedExtern handle, WasmTableType type) : WasmTable
{
    public WasmtimeStore Store { get; } = store;

    public WasmtimeIndexedExtern Handle { get; } = handle;

    public override WasmTableType Type { get; } = type;

    public override ulong Size => Store.GetTableSize(this);

    public override WasmValue Get(ulong index) => Store.GetTableElement(this, index);

    public override void Set(ulong index, WasmValue value) => Store.SetTableElement(this, index, value);

    public override ulong Grow(ulong delta, WasmValue value) => Store.GrowTable(this, delta, value);
}

internal sealed class WasmtimeGlobal(WasmtimeStore store, WasmtimeGlobalHandle handle, WasmGlobalType type) : WasmGlobal
{
    public WasmtimeStore Store { get; } = store;

    public WasmtimeGlobalHandle Handle { get; } = handle;

    public override WasmGlobalType Type { get; } = type;

    public override WasmValue Get() => Store.GetGlobalValue(this);

    public override void Set(WasmValue value) => Store.SetGlobalValue(this, value);
}

internal sealed class WasmtimeTag(WasmtimeStore store, WasmtimeIndexedExtern handle, WasmTagType type) : WasmTag
{
    public WasmtimeStore Store { get; } = store;

    public WasmtimeIndexedExtern Handle { get; } = handle;

    public override WasmTagType Type { get; } = type;
}

/// <summary>A struct or an array: the reference is rooted while the object is alive.</summary>
internal sealed class WasmtimeGcObject(WasmtimeStore store, RootedReferenceHandle reference, bool isArray) : WasmGcObject
{
    public WasmtimeStore Store { get; } = store;

    public WasmtimeRef Handle
    {
        get
        {
            var result = reference.Reference;
            GC.KeepAlive(this);
            return result;
        }
    }

    public override bool IsArray { get; } = isArray;
}

/// <summary>An exception: the reference is rooted while the object is alive.</summary>
internal sealed class WasmtimeExceptionReference(WasmtimeStore store, RootedReferenceHandle reference) : WasmExceptionReference
{
    private WasmTag? _tag;

    public WasmtimeStore Store { get; } = store;

    public WasmtimeRef Handle
    {
        get
        {
            var result = reference.Reference;
            GC.KeepAlive(this);
            return result;
        }
    }

    public override WasmTag Tag => _tag ??= Store.GetExceptionTag(this);

    public override int ArgumentCount => Store.GetExceptionArgumentCount(this);

    public override WasmValue GetArgument(int index) => Store.GetExceptionArgument(this, index);
}
