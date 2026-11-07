namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Bytes charged to an <see cref="AllocationScope"/> ahead of renting, so that a multi-buffer allocation (all the slabs
/// of a storage, or all the replacement storages of a transaction) is checked against the limit once, before any buffer
/// is rented. Disposing the reservation releases the unconsumed bytes. Not thread-safe: one reservation serves one
/// operation.
/// </summary>
internal sealed class AllocationReservation : IDisposable
{
    internal AllocationReservation(AllocationScope scope, AllocationKind kind, long bytes)
    {
        Scope = scope;
        Kind = kind;
        RemainingBytes = bytes;
    }

    public AllocationScope Scope { get; }

    public AllocationKind Kind { get; }

    /// <summary>Gets the bytes still reserved and not yet consumed by a rental.</summary>
    public long RemainingBytes { get; private set; }

    /// <summary>Rents a buffer whose capacity is taken from the reservation.</summary>
    /// <exception cref="InvalidOperationException">The reservation does not cover the capacity of the buffer.</exception>
    /// <exception cref="OutOfMemoryException">The buffer cannot be allocated; the reservation is unchanged.</exception>
    public PooledBuffer Rent(int length, bool clear)
    {
        var capacity = Scope.Pool.GetRentCapacity(length);
        if (capacity > RemainingBytes)
            throw new InvalidOperationException("The allocation reservation does not cover the requested buffer.");

        var buffer = Scope.Pool.Rent(length, clear);
        RemainingBytes -= capacity;
        Scope.OnReservedAllocationCreated(capacity);
        return new PooledBuffer(Scope, Kind, buffer, length);
    }

    /// <summary>Returns <see langword="true"/> if the reservation covers a buffer of <paramref name="length"/> bytes.</summary>
    public bool Covers(long length) => Scope.Pool.GetRentCapacity(length) <= RemainingBytes;

    public void Dispose()
    {
        var remaining = RemainingBytes;
        if (remaining > 0)
        {
            RemainingBytes = 0;
            Scope.ReleaseReserved(remaining, Kind);
        }
    }
}
