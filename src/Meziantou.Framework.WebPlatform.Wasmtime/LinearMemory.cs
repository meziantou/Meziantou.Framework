using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Wasmtime.Interop;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>The limits of the memories of an engine (the state of its memory creator).</summary>
internal sealed class LinearMemoryLimits(long maxMemorySize)
{
    public long MaxMemorySize { get; } = maxMemorySize;
}

/// <summary>
/// The bytes of a memory that is not shared: a pinned .NET array with the exact size of the memory, which the
/// <c>ArrayBuffer</c> objects of JavaScript share (the memory creator of the engine allocates them,
/// https://docs.wasmtime.dev/c-api/config_8h.html#a4dfe68d7d9f1e1e8fcf5e74d31d5df60). The array is replaced when the
/// memory grows: the memory moves.
/// </summary>
/// <remarks>
/// The native memory references the object by a weak handle: the store that created it keeps it alive, so that the
/// objects of a store that is not disposed can be collected.
/// </remarks>
internal sealed unsafe class LinearMemory
{
    private readonly ulong _maximumByteSize;
    private byte[] _data;

    private LinearMemory(ulong byteSize, ulong maximumByteSize)
    {
        _maximumByteSize = maximumByteSize;
        _data = GC.AllocateArray<byte>(checked((int)byteSize), pinned: true);
    }

    /// <summary>The bytes of the memory.</summary>
    public byte[] Data => _data;

    /// <summary>The address of the bytes of the memory, which identifies the memory in its store.</summary>
    public nint DataPointer => (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_data));

    /// <summary>Called after the array is replaced, with the previous address.</summary>
    public Action<LinearMemory, nint>? Moved { get; set; }

    /// <summary>The store that creates the memories on the current thread (set while a store instantiates a module or creates a memory).</summary>
    [ThreadStatic]
    internal static WasmtimeStore? s_creatingStore;

    /// <summary><c>wasmtime_new_memory_callback_t</c></summary>
    [UnmanagedCallersOnly]
    internal static nint NewMemory(nint env, nint type, nuint minimum, nuint maximum, nuint reservedSize, nuint guardSize, WasmtimeLinearMemory* result)
    {
        try
        {
            var limits = (LinearMemoryLimits)GCHandle.FromIntPtr(env).Target!;
            var maximumByteSize = Math.Min((ulong)maximum, (ulong)limits.MaxMemorySize);
            if ((ulong)minimum > maximumByteSize)
                return NativeErrors.CreateError($"The memory cannot be larger than {limits.MaxMemorySize} bytes");

            var memory = new LinearMemory(minimum, maximumByteSize);
            var store = s_creatingStore;
            store?.AddLinearMemory(memory);

            // A memory created outside a store operation is kept alive by its handle (it is not expected)
            var handle = GCHandle.Alloc(memory, store is null ? GCHandleType.Normal : GCHandleType.Weak);
            result->Env = GCHandle.ToIntPtr(handle);
            result->GetMemory = &GetMemory;
            result->GrowMemory = &GrowMemory;
            result->Finalizer = &FreeHandle;
            return 0;
        }
        catch (Exception ex)
        {
            return NativeErrors.CreateError(ex.Message);
        }
    }

    /// <summary><c>wasmtime_memory_get_callback_t</c></summary>
    [UnmanagedCallersOnly]
    private static byte* GetMemory(nint env, nuint* byteSize, nuint* byteCapacity)
    {
        var memory = (LinearMemory)GCHandle.FromIntPtr(env).Target!;
        var data = memory._data;
        *byteSize = (nuint)data.Length;
        *byteCapacity = (nuint)data.Length;
        return (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(data));
    }

    /// <summary><c>wasmtime_memory_grow_callback_t</c>: an error makes the growth fail (memory.grow returns -1).</summary>
    [UnmanagedCallersOnly]
    private static nint GrowMemory(nint env, nuint newSize)
    {
        try
        {
            var memory = (LinearMemory)GCHandle.FromIntPtr(env).Target!;
            if ((ulong)newSize > memory._maximumByteSize)
                return NativeErrors.CreateError("The memory cannot grow");

            var previousPointer = memory.DataPointer;
            var data = GC.AllocateArray<byte>(checked((int)newSize), pinned: true);
            memory._data.AsSpan().CopyTo(data);
            memory._data = data;
            memory.Moved?.Invoke(memory, previousPointer);
            return 0;
        }
        catch (Exception ex)
        {
            return NativeErrors.CreateError(ex.Message);
        }
    }

    [UnmanagedCallersOnly]
    private static void FreeHandle(nint env) => GCHandle.FromIntPtr(env).Free();

    /// <summary>The finalizer of the memory creator: it frees the handle of its limits.</summary>
    [UnmanagedCallersOnly]
    internal static void FreeLimits(nint env) => GCHandle.FromIntPtr(env).Free();
}
