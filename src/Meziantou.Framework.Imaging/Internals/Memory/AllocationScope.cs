using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Live-byte accounting of library-controlled buffers, bounded by
/// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>. Buffers are charged at their <em>actual</em> rented capacity
/// (the size class of <see cref="SlabPool"/>), before they are rented; a failed charge leaves the scope unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Each independently constructed, loaded, imported or cloned image has its own scope. A reader has one scope shared by its
/// working state and the images it returns: they stay charged to it until each of them is disposed, even after the reader
/// itself is disposed. A scope therefore has no disposal of its own; it is empty once every charge has been released.
/// </para>
/// <para>
/// The scope is thread-safe, because images sharing a reader scope may be disposed concurrently. It bounds one owner's live
/// bytes; it is neither a process-wide memory cap nor a CPU-time cap. Pool retention is bounded separately by
/// <see cref="SlabPoolOptions"/>.
/// </para>
/// </remarks>
internal sealed class AllocationScope
{
    private static long s_nextId;

    private readonly Lock _lock = new();
    private readonly long[] _liveBytesByKind = new long[(int)AllocationKind.Metadata + 1];
    private long _liveBytes;
    private long _reservedBytes;
    private long _peakLiveBytes;
    private int _liveAllocations;
    private long _totalAllocations;
    private long _rejectedRequests;

    public AllocationScope(ImageResourceLimits limits, SlabPool? pool = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        Limits = limits;
        Pool = pool ?? SlabPool.Shared;
        Id = Interlocked.Increment(ref s_nextId);
        Name = name ?? "scope";
    }

    /// <summary>Gets a unique identifier, for diagnostics.</summary>
    public long Id { get; }

    /// <summary>Gets a descriptive name, for diagnostics.</summary>
    public string Name { get; }

    public ImageResourceLimits Limits { get; }

    public SlabPool Pool { get; }

    /// <summary>Gets the configured live-byte limit.</summary>
    public long Limit => Limits.MaxLiveAllocationBytes;

    /// <summary>Gets the number of live bytes currently charged (including unconsumed reservations).</summary>
    public long LiveBytes
    {
        get
        {
            lock (_lock)
            {
                return _liveBytes;
            }
        }
    }

    /// <summary>Creates a scope for the limits of a configuration.</summary>
    public static AllocationScope Create(ImageConfiguration? configuration, string? name = null)
    {
        // Test-only ambient hook (fault injection and leak audits through the public API); null in production
        var tracking = AllocationTracking.Current;
        var scope = new AllocationScope((configuration ?? ImageConfiguration.Default).Limits, tracking?.Pool, name);
        tracking?.Register(scope);
        return scope;
    }

    /// <summary>Gets the number of bytes the scope would charge for a buffer of <paramref name="length"/> bytes.</summary>
    public long GetChargedSize(long length) => Pool.GetRentCapacity(length);

    /// <summary>Charges and rents one buffer.</summary>
    /// <param name="length">The requested length.</param>
    /// <param name="kind">The accounting category.</param>
    /// <param name="clear">
    /// <see langword="true"/> to zero the requested length. Only internal scratch storage that is fully written before
    /// being read may pass <see langword="false"/>.
    /// </param>
    /// <exception cref="ImageResourceLimitException">The charge would exceed the limit; nothing is charged or rented.</exception>
    public PooledBuffer Rent(int length, AllocationKind kind, bool clear = true)
    {
        using var reservation = Reserve(GetChargedSize(length), kind);
        return reservation.Rent(length, clear);
    }

    /// <summary>Charges <paramref name="bytes"/> up front so that several buffers can then be rented from the reservation without further limit checks.</summary>
    /// <exception cref="ImageResourceLimitException">The charge would exceed the limit; nothing is charged.</exception>
    public AllocationReservation Reserve(long bytes, AllocationKind kind)
    {
        ChargeCore(bytes, kind, isReservation: true);
        return new AllocationReservation(this, kind, bytes);
    }

    /// <summary>
    /// Charges library-controlled bytes that do not come from the pool (for example retained metadata arrays), at their
    /// actual allocated size. Dispose the returned charge when the bytes are released.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The charge would exceed the limit; nothing is charged.</exception>
    public AllocationCharge Charge(long bytes, AllocationKind kind)
    {
        ChargeCore(bytes, kind, isReservation: false);
        return new AllocationCharge(this, kind, bytes);
    }

    public AllocationScopeDiagnostics GetDiagnostics()
    {
        lock (_lock)
        {
            return new AllocationScopeDiagnostics(Limit, _liveBytes, _reservedBytes, _peakLiveBytes, _liveAllocations, _totalAllocations, _rejectedRequests);
        }
    }

    /// <summary>Gets the live bytes of one category (reservations included).</summary>
    public long GetLiveBytes(AllocationKind kind)
    {
        lock (_lock)
        {
            return _liveBytesByKind[(int)kind];
        }
    }

    public override string ToString() => $"{Name} #{Id}";

    private void ChargeCore(long bytes, AllocationKind kind, bool isReservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_lock)
        {
            if (!CheckedSizes.TryAdd(_liveBytes, bytes, out var requested))
            {
                _rejectedRequests++;
                throw CheckedSizes.CreateOverflowException(Limits);
            }

            if (requested > Limit)
            {
                _rejectedRequests++;
                Limits.EnsureLiveAllocationWithinLimit(requested);
            }

            _liveBytes = requested;
            _liveBytesByKind[(int)kind] += bytes;
            _peakLiveBytes = Math.Max(_peakLiveBytes, requested);
            if (isReservation)
            {
                _reservedBytes += bytes;
            }
            else
            {
                _liveAllocations++;
                _totalAllocations++;
            }
        }
    }

    /// <summary>Converts reserved bytes into one live allocation (a buffer rented from a reservation).</summary>
    internal void OnReservedAllocationCreated(long bytes)
    {
        lock (_lock)
        {
            Debug.Assert(_reservedBytes >= bytes);
            _reservedBytes -= bytes;
            _liveAllocations++;
            _totalAllocations++;
        }
    }

    /// <summary>Releases the unconsumed part of a reservation.</summary>
    internal void ReleaseReserved(long bytes, AllocationKind kind)
    {
        lock (_lock)
        {
            Debug.Assert(_reservedBytes >= bytes && _liveBytes >= bytes);
            _reservedBytes -= bytes;
            _liveBytes -= bytes;
            _liveBytesByKind[(int)kind] -= bytes;
        }
    }

    /// <summary>Releases one live allocation.</summary>
    internal void ReleaseAllocation(long bytes, AllocationKind kind)
    {
        lock (_lock)
        {
            Debug.Assert(_liveBytes >= bytes && _liveAllocations > 0);
            _liveBytes -= bytes;
            _liveBytesByKind[(int)kind] -= bytes;
            _liveAllocations--;
        }
    }
}
