using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Wasmtime.Interop;
using Meziantou.Framework.WebPlatform.WebAssembly;

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
            if (NativeMethods.wasmtime_memorytype_isshared(type))
                return SharedLinearMemory.Create(limits, (ulong)minimum, (ulong)maximum, result);

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
    /// <remarks>
    /// The array is pinned (allocated in the pinned object heap) and the memory keeps it alive until it is replaced, so its
    /// address stays valid while wasmtime uses it. The handle is weak: no memory (a null pointer) when the memory was
    /// collected, which does not happen while its store is alive.
    /// </remarks>
    [UnmanagedCallersOnly]
    private static byte* GetMemory(nint env, nuint* byteSize, nuint* byteCapacity)
    {
        try
        {
            if (GCHandle.FromIntPtr(env).Target is LinearMemory memory)
            {
                var data = memory._data;
                *byteSize = (nuint)data.Length;
                *byteCapacity = (nuint)data.Length;
                return (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(data));
            }
        }
        catch
        {
            // An exception must not cross the native frames
        }

        *byteSize = 0;
        *byteCapacity = 0;
        return null;
    }

    /// <summary><c>wasmtime_memory_grow_callback_t</c>: an error makes the growth fail (memory.grow returns -1).</summary>
    [UnmanagedCallersOnly]
    private static nint GrowMemory(nint env, nuint newSize)
    {
        LinearMemory memory;
        nint previousPointer;
        try
        {
            if (GCHandle.FromIntPtr(env).Target is not LinearMemory target)
                return NativeErrors.CreateError("The memory was collected");

            memory = target;
            if ((ulong)newSize > memory._maximumByteSize)
                return NativeErrors.CreateError("The memory cannot grow");

            previousPointer = memory.DataPointer;
            var data = GC.AllocateArray<byte>(checked((int)newSize), pinned: true);
            memory._data.AsSpan().CopyTo(data);
            memory._data = data;
        }
        catch (Exception ex)
        {
            return NativeErrors.CreateError(ex.Message);
        }

        // The memory moved: the growth succeeded, whatever the outcome of the notification (wasmtime reads the new size
        // from GetMemory). The store updates its index of the memories before it notifies the wrapper of the memory.
        try
        {
            memory.Moved?.Invoke(memory, previousPointer);
        }
        catch
        {
            // An exception must not cross the native frames
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static void FreeHandle(nint env) => GCHandle.FromIntPtr(env).Free();

    /// <summary>The finalizer of the memory creator: it frees the handle of its limits.</summary>
    [UnmanagedCallersOnly]
    internal static void FreeLimits(nint env) => GCHandle.FromIntPtr(env).Free();
}

/// <summary>
/// The bytes of a shared memory: a growable shared data block of the script engine (<see cref="WasmSharedMemoryBytes"/>),
/// created by the host of the store that creates the memory. It is allocated for the maximum size of the memory (limited
/// by <see cref="WasmtimeRuntimeOptions.MaxMemorySize"/>) and never moves, as Wasmtime requires for a shared memory.
/// </summary>
/// <remarks>
/// A shared memory can outlive the store that created it (the other stores that import it, the <c>wasmtime_sharedmemory_t</c>
/// references): the native memory references the object by a strong handle, freed when Wasmtime deletes the memory.
/// </remarks>
internal sealed unsafe class SharedLinearMemory
{
    private const ulong PageSize = 65536;

    // The shared memories by address (the bytes never move), to find the bytes of a wasmtime_sharedmemory_t
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, SharedLinearMemory> Memories = new();

    private SharedLinearMemory(WasmSharedMemoryBytes bytes)
    {
        Bytes = bytes;
    }

    public WasmSharedMemoryBytes Bytes { get; }

    /// <summary>The wrapper of the memory (see <see cref="WasmtimeRuntime.GetSharedMemory"/>): weak, so that it does not keep the native memory alive.</summary>
    public WeakReference<WasmtimeSharedMemory>? Wrapper { get; set; }

    // The size a growth of the current thread must reach (see BeginExactGrowth), and whether another growth came first
    [ThreadStatic]
    private static ulong s_exactGrowthByteSize;

    [ThreadStatic]
    private static bool s_exactGrowthRaced;

    /// <summary>
    /// Makes the growths of the shared memories by the current thread fail when they would not reach exactly
    /// <paramref name="byteSize"/> bytes (another agent grew the memory since its size was read), until
    /// <see cref="EndExactGrowth"/>. Wasmtime computes the new size and calls <see cref="GrowMemory"/> under the lock of the
    /// memory, on the thread that grows it.
    /// </summary>
    public static void BeginExactGrowth(ulong byteSize)
    {
        s_exactGrowthByteSize = byteSize;
        s_exactGrowthRaced = false;
    }

    /// <summary>Ends <see cref="BeginExactGrowth"/>, and returns whether a growth failed because another growth came first.</summary>
    public static bool EndExactGrowth()
    {
        s_exactGrowthByteSize = 0;
        return s_exactGrowthRaced;
    }

    /// <summary>Returns the shared memory whose bytes start at <paramref name="address"/>, or null.</summary>
    public static SharedLinearMemory? Find(nint address) => Memories.TryGetValue(address, out var memory) ? memory : null;

    /// <summary>The <c>wasmtime_new_memory_callback_t</c> of a shared memory.</summary>
    internal static nint Create(LinearMemoryLimits limits, ulong minimum, ulong maximum, WasmtimeLinearMemory* result)
    {
        // The data blocks of the script engines are .NET arrays: the capacity is a whole number of pages
        var capacity = Math.Min(Math.Min(maximum, (ulong)limits.MaxMemorySize), (ulong)Array.MaxLength) / PageSize * PageSize;
        if (minimum > capacity)
            return NativeErrors.CreateError($"The shared memory cannot be larger than {capacity} bytes");

        if (LinearMemory.s_creatingStore is not { } store)
            return NativeErrors.CreateError("A shared memory can only be created by a store");

        WasmSharedMemoryBytes bytes;
        try
        {
            bytes = store.CreateSharedMemoryBytes(minimum, capacity);
        }
        catch (Exception ex)
        {
            // The exception of the host (e.g. a RangeError, or the memory limit of the script engine) is thrown by the operation of the store
            store.SetPendingException(ex);
            return NativeErrors.CreateError(ex.Message);
        }

        var memory = new SharedLinearMemory(bytes);
        Memories[memory.Bytes.Address] = memory;
        result->Env = GCHandle.ToIntPtr(GCHandle.Alloc(memory));
        result->GetMemory = &GetMemory;
        result->GrowMemory = &GrowMemory;
        result->Finalizer = &Free;
        return 0;
    }

    /// <summary><c>wasmtime_memory_get_callback_t</c>: the capacity is the size of the data block, so the memory never moves.</summary>
    [UnmanagedCallersOnly]
    private static byte* GetMemory(nint env, nuint* byteSize, nuint* byteCapacity)
    {
        try
        {
            var bytes = ((SharedLinearMemory)GCHandle.FromIntPtr(env).Target!).Bytes;
            *byteSize = (nuint)bytes.ByteLength;
            *byteCapacity = (nuint)bytes.Capacity;
            return (byte*)bytes.Address;
        }
        catch
        {
            // An exception must not cross the native frames
            *byteSize = 0;
            *byteCapacity = 0;
            return null;
        }
    }

    /// <summary><c>wasmtime_memory_grow_callback_t</c>: the data block grows in place (memory.grow returns -1 beyond its capacity).</summary>
    [UnmanagedCallersOnly]
    private static nint GrowMemory(nint env, nuint newSize)
    {
        try
        {
            if (s_exactGrowthByteSize != 0 && (ulong)newSize != s_exactGrowthByteSize)
            {
                s_exactGrowthRaced = true;
                return NativeErrors.CreateError("The shared memory grew concurrently");
            }

            var bytes = ((SharedLinearMemory)GCHandle.FromIntPtr(env).Target!).Bytes;
            return bytes.TryGrow((ulong)newSize) ? 0 : NativeErrors.CreateError("The shared memory cannot grow");
        }
        catch (Exception ex)
        {
            return NativeErrors.CreateError(ex.Message);
        }
    }

    [UnmanagedCallersOnly]
    private static void Free(nint env)
    {
        var handle = GCHandle.FromIntPtr(env);
        if (handle.Target is SharedLinearMemory memory)
        {
            Memories.TryRemove(new KeyValuePair<nint, SharedLinearMemory>(memory.Bytes.Address, memory));
        }

        handle.Free();
    }
}
