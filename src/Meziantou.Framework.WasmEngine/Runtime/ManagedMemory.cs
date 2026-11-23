using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// A memory. The bytes of a memory that is not shared are a pinned .NET array with the exact size of the memory, replaced
/// when it grows. The bytes of a shared memory are a <see cref="WasmSharedMemoryBlock"/> allocated for the maximum size
/// of the memory, which never moves.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0055:Do not use finalizer", Justification = "The memory usage metric is decremented when the memory is collected")]
internal sealed unsafe class ManagedMemory : WasmMemory
{
    private readonly WasmEngineTelemetry _telemetry;
    private readonly Lock _growLock = new();
    private readonly ulong _maxByteLength;
    private byte[] _data;
    private WasmSharedMemoryBlock? _sharedBlock;
    private long _reportedBytes;
    private ulong _byteLength;

    private ManagedMemory(ManagedStore? store, WasmMemoryType type, MemoryInfo info, WasmEngineTelemetry telemetry, ulong maxByteLength)
    {
        Store = store;
        Type = type;
        Info = info;
        _telemetry = telemetry;
        _maxByteLength = maxByteLength;
        _data = [];
    }

    ~ManagedMemory()
    {
        _telemetry.RecordMemoryUsage(-_reportedBytes, Info.Shared);
    }

    /// <summary>The store of a memory that is not shared, or null.</summary>
    public ManagedStore? Store { get; }

    public MemoryInfo Info { get; }

    public override WasmMemoryType Type { get; }

    public bool Is64 => Info.Is64;

    /// <summary>The address of the first byte (it changes when a memory that is not shared grows).</summary>
    public byte* Base { get; private set; }

    /// <summary>The size in bytes.</summary>
    public ulong ByteLength => Info.Shared ? SharedBlock!.ByteLength : _byteLength;

    /// <summary>The size in bytes of a memory that is not shared (the compiled code reads it, see <see cref="IlCompiler"/>).</summary>
    public ulong UnsharedByteLength => _byteLength;

    public override byte[] Data => Info.Shared ? throw new NotSupportedException("The bytes of a shared memory are a shared memory block (see SharedBlock)") : _data;

    public override WasmSharedMemoryBlock? SharedBlock => _sharedBlock;

    public override Span<byte> GetSpan() => Info.Shared ? _sharedBlock!.GetSpan() : _data;

    public override ulong Size => ByteLength / WasmMemoryType.PageSize;

    /// <exception cref="WasmAllocationException">The memory cannot be allocated.</exception>
    public static ManagedMemory Create(ManagedStore store, MemoryInfo info)
    {
        var runtime = store.Runtime;
        var pageLimit = info.Maximum ?? (info.Is64 ? 1UL << 48 : 65536UL);
        var limit = (ulong)Math.Min(runtime.Options.MaxMemorySize, Array.MaxLength);
        var maxByteLength = pageLimit >= limit / WasmMemoryType.PageSize ? limit / WasmMemoryType.PageSize * WasmMemoryType.PageSize : pageLimit * WasmMemoryType.PageSize;
        var initial = info.Minimum > maxByteLength / WasmMemoryType.PageSize ? throw new WasmAllocationException("The memory cannot be larger than " + maxByteLength + " bytes") : info.Minimum * WasmMemoryType.PageSize;
        var type = new WasmMemoryType(info.Minimum, info.Maximum, info.Is64, info.Shared);
        if (info.Shared)
        {
            var block = (store.Options.SharedMemoryAllocator ?? PinnedArraySharedMemoryBlock.Create)(initial, maxByteLength);
            if (block.ByteLength < initial || block.Capacity < block.ByteLength)
                throw new WasmAllocationException("The shared memory allocator returned a block smaller than the memory");

            var shared = new ManagedMemory(store: null, type, info, runtime.Telemetry, Math.Min(maxByteLength, block.Capacity / WasmMemoryType.PageSize * WasmMemoryType.PageSize))
            {
                Base = (byte*)block.Address,
                _sharedBlock = block,
                _reportedBytes = (long)block.Capacity,
            };
            runtime.Telemetry.RecordMemoryUsage((long)block.Capacity, shared: true);
            return shared;
        }

        var memory = new ManagedMemory(store, type, info, runtime.Telemetry, maxByteLength);
        memory.SetData(GC.AllocateArray<byte>(checked((int)initial), pinned: true));
        return memory;
    }

    private void SetData(byte[] data)
    {
        _data = data;
        Base = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(data));
        Volatile.Write(ref _byteLength, (ulong)data.LongLength);
        var delta = data.LongLength - _reportedBytes;
        _reportedBytes = data.LongLength;
        _telemetry.RecordMemoryUsage(delta, shared: false);
    }

    /// <summary>Grows the memory by <paramref name="delta"/> pages, and returns its previous size in pages, or -1 when it cannot grow (memory.grow).</summary>
    public long TryGrow(ulong delta)
    {
        if (Info.Shared)
        {
            lock (_growLock)
            {
                var size = _sharedBlock!.ByteLength;
                var pages = size / WasmMemoryType.PageSize;
                if (delta == 0)
                    return (long)pages;

                if (delta > (_maxByteLength - size) / WasmMemoryType.PageSize)
                    return -1;

                return _sharedBlock.TryGrow(size + (delta * WasmMemoryType.PageSize)) ? (long)pages : -1;
            }
        }

        var length = _byteLength;
        var previous = length / WasmMemoryType.PageSize;
        if (delta == 0)
            return (long)previous;

        if (delta > (_maxByteLength - length) / WasmMemoryType.PageSize)
            return -1;

        byte[] data;
        try
        {
            data = GC.AllocateArray<byte>(checked((int)(length + (delta * WasmMemoryType.PageSize))), pinned: true);
        }
        catch (OutOfMemoryException)
        {
            return -1;
        }

        _data.AsSpan().CopyTo(data);
        SetData(data);
        OnDataChanged();
        return (long)previous;
    }

    public override ulong Grow(ulong delta)
    {
        Store?.ThrowIfDisposed();
        var previous = TryGrow(delta);
        if (previous < 0)
            throw new WasmAllocationException("The memory cannot grow");

        return (ulong)previous;
    }

    /// <summary>Grows a shared memory to exactly <paramref name="newSize"/> pages when it is smaller (the threads may grow it concurrently).</summary>
    public override ulong GrowTo(ulong newSize)
    {
        if (!Info.Shared)
            return base.GrowTo(newSize);

        lock (_growLock)
        {
            var size = Size;
            if (size >= newSize)
                return size;

            if (TryGrow(newSize - size) < 0)
                throw new WasmAllocationException("The memory cannot grow");

            return size;
        }
    }

    /// <summary>Returns the address of an access of <paramref name="size"/> bytes, or throws a trap when it is out of bounds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte* GetAddress(ulong address, ulong offset, int size)
    {
        var effective = address + offset;
        var length = ByteLength;
        if (effective < address || effective > length || length - effective < (ulong)size)
            throw Traps.OutOfBoundsMemory();

        return Base + effective;
    }

    /// <summary>Returns the address of an atomic access: it must be aligned.</summary>
    public byte* GetAtomicAddress(ulong address, ulong offset, int size)
    {
        var pointer = GetAddress(address, offset, size);
        if (((address + offset) & (ulong)(size - 1)) != 0)
            throw Traps.UnalignedAtomic();

        return pointer;
    }

    /// <summary>Checks that the range is in bounds (the bulk memory instructions), and returns its address.</summary>
    public byte* GetRange(ulong address, ulong length)
    {
        var size = ByteLength;
        if (address > size || length > size - address)
            throw Traps.OutOfBoundsMemory();

        return Base + address;
    }

    public override uint AtomicNotify(ulong address, uint count)
    {
        if (!Info.Shared)
            throw new NotSupportedException("The memory is not shared");

        var pointer = GetAtomicAddress(address, 0, 4);
        return AtomicWaiters.Notify((nint)pointer, count);
    }

    public override WasmWaitResult AtomicWait(ulong address, long expected, int size, TimeSpan? timeout, WasmWaitSignal? signal = null)
    {
        if (!Info.Shared)
            throw new NotSupportedException("The memory is not shared");

        var pointer = GetAtomicAddress(address, 0, size);
        return AtomicWaiters.Wait(pointer, expected, size, timeout, signal);
    }

    internal void RaiseDataChanged() => OnDataChanged();
}

/// <summary>
/// The waiters of the addresses of the shared memories (memory.atomic.wait and memory.atomic.notify). The lists of
/// waiters are protected by one lock, and each waiter waits on its own lock: a notification wakes only the waiters it
/// notifies. The locks are taken in this order: the lock of the lists, then the lock of a waiter.
/// </summary>
internal static unsafe class AtomicWaiters
{
    private static readonly Lock ListsLock = new();
    private static readonly Dictionary<nint, List<Waiter>> Waiters = [];

    private sealed class Waiter
    {
        public bool Notified;
    }

    public static WasmWaitResult Wait(byte* address, long expected, int size, TimeSpan? timeout, WasmWaitSignal? signal)
    {
        // The browser (WebAssembly) cannot block its thread
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("memory.atomic.wait is not supported on the browser.");

        var waiter = new Waiter();
        lock (ListsLock)
        {
            // The value is compared under the lock of the lists, which the notifications take: no notification is lost
            var value = size == 4 ? Volatile.Read(ref *(int*)address) : Volatile.Read(ref *(long*)address);
            if (size == 4 ? (int)value != (int)expected : value != expected)
                return WasmWaitResult.NotEqual;

            if (!Waiters.TryGetValue((nint)address, out var list))
            {
                list = [];
                Waiters.Add((nint)address, list);
            }

            list.Add(waiter);
            signal?.Attach([1]);
        }

        var deadline = timeout is { } duration ? System.Diagnostics.Stopwatch.GetTimestamp() + (long)(Math.Min(duration.TotalSeconds, 1e9) * System.Diagnostics.Stopwatch.Frequency) : long.MaxValue;
        while (true)
        {
            lock (waiter)
            {
                if (waiter.Notified)
                    return WasmWaitResult.Ok;

                var remaining = deadline == long.MaxValue ? Timeout.Infinite : (int)Math.Clamp((deadline - System.Diagnostics.Stopwatch.GetTimestamp()) * 1000 / System.Diagnostics.Stopwatch.Frequency, 0, int.MaxValue - 1);
                if (remaining != 0)
                {
                    Monitor.Wait(waiter, remaining);
                    continue;
                }
            }

            // The timeout elapsed: the waiter leaves the list, unless a notification took it meanwhile
            lock (ListsLock)
            {
                lock (waiter)
                {
                    if (waiter.Notified)
                        return WasmWaitResult.Ok;
                }

                if (Waiters.TryGetValue((nint)address, out var list))
                {
                    list.Remove(waiter);
                    if (list.Count == 0)
                    {
                        Waiters.Remove((nint)address);
                    }
                }

                return WasmWaitResult.TimedOut;
            }
        }
    }

    public static uint Notify(nint address, uint count)
    {
        if (count == 0)
            return 0;

        lock (ListsLock)
        {
            if (!Waiters.TryGetValue(address, out var list))
                return 0;

            uint notified = 0;
            while (notified < count && list.Count > 0)
            {
                var waiter = list[0];
                list.RemoveAt(0);
                lock (waiter)
                {
                    waiter.Notified = true;
                    Monitor.Pulse(waiter);
                }

                notified++;
            }

            if (list.Count == 0)
            {
                Waiters.Remove(address);
            }

            return notified;
        }
    }
}
