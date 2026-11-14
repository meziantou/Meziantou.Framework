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

/// <summary>
/// A shared memory (<c>wasmtime_sharedmemory_t</c>): it is not bound to a store, so it can be imported by the modules of
/// any agent, and its methods can be called from any thread. Its bytes are the data block of <see cref="SharedLinearMemory"/>.
/// </summary>
internal sealed class WasmtimeSharedMemory(WasmtimeRuntime runtime, SharedMemoryHandle handle, WasmSharedMemoryBytes bytes, WasmMemoryType type) : WasmMemory
{
    public WasmtimeRuntime Runtime { get; } = runtime;

    public SharedMemoryHandle Handle { get; } = handle;

    public override WasmMemoryType Type { get; } = type;

    public override byte[] Data => throw new NotSupportedException("The bytes of a shared memory are a shared data block");

    public override WasmSharedMemoryBytes SharedBytes { get; } = bytes;

    public override ulong Size
    {
        get
        {
            using var memory = new SafeHandleLease(Handle);
            return NativeMethods.wasmtime_sharedmemory_size(memory.Value);
        }
    }

    public override unsafe ulong Grow(ulong delta)
    {
        using var memory = new SafeHandleLease(Handle);
        ulong previousSize;
        var error = NativeMethods.wasmtime_sharedmemory_grow(memory.Value, delta, &previousSize);
        if (error != 0)
            throw new WasmAllocationException(NativeErrors.TakeErrorMessage(error));

        return previousSize;
    }

    /// <summary>Grows the memory to exactly <paramref name="newSize"/> pages: a concurrent growth makes the growth fail, and it starts again.</summary>
    public override unsafe ulong GrowTo(ulong newSize)
    {
        using var memory = new SafeHandleLease(Handle);
        while (true)
        {
            var size = NativeMethods.wasmtime_sharedmemory_size(memory.Value);
            if (size >= newSize)
                return size;

            ulong previousSize;
            nint error;
            bool raced;
            SharedLinearMemory.BeginExactGrowth(newSize * WasmPageSize);
            try
            {
                error = NativeMethods.wasmtime_sharedmemory_grow(memory.Value, newSize - size, &previousSize);
            }
            finally
            {
                raced = SharedLinearMemory.EndExactGrowth();
            }

            if (error == 0)
                return previousSize;

            var message = NativeErrors.TakeErrorMessage(error);
            if (!raced)
                throw new WasmAllocationException(message);
        }
    }

    private const ulong WasmPageSize = 65536;

    public override uint AtomicNotify(ulong address, uint count) => Runtime.AtomicNotify(this, address, count);

    public override WasmWaitResult AtomicWait(ulong address, long expected, int size, TimeSpan? timeout, WasmWaitSignal? signal = null) => Runtime.AtomicWait(this, address, expected, size, timeout, signal);
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

    /// <summary>Returns a lease on the rooted reference: its value is a <c>WasmtimeRef*</c>.</summary>
    public SafeHandleLease EnterReference() => new(reference);

    public override bool IsArray { get; } = isArray;
}

/// <summary>An exception: the reference is rooted while the object is alive.</summary>
internal sealed class WasmtimeExceptionReference(WasmtimeStore store, RootedReferenceHandle reference) : WasmExceptionReference
{
    private WasmTag? _tag;

    public WasmtimeStore Store { get; } = store;

    /// <summary>Returns a lease on the rooted reference: its value is a <c>WasmtimeRef*</c>.</summary>
    public SafeHandleLease EnterReference() => new(reference);

    public override WasmTag Tag => _tag ??= Store.GetExceptionTag(this);

    public override int ArgumentCount => Store.GetExceptionArgumentCount(this);

    public override WasmValue GetArgument(int index) => Store.GetExceptionArgument(this, index);
}
