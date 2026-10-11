namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Segmented pixel storage of one frame: rows are contiguous, grouped into row-aligned slabs
/// rented from the pool of the owner's <see cref="AllocationScope"/>; the frame as a whole is not contiguous. Every byte of
/// every slab that a row can expose is zeroed at allocation, and padding between rows is never exposed: a row span contains
/// exactly the visible bytes of that row.
/// </summary>
/// <remarks>
/// Rows are only reachable through an exclusive <see cref="PixelLease"/> (<see cref="AcquireLease"/>), so the slabs cannot be
/// returned to the pool while a caller can still see them.
/// </remarks>
internal sealed class PixelStorage
{
    private static long s_nextId;

    private readonly PooledBuffer[] _slabs;
    private readonly byte[][] _slabArrays;
    private readonly int _rowsPerSlab;
    private readonly int _stride;
    private bool _disposed;
    private long _leaseToken;

    private PixelStorage(StorageOwner owner, PixelStorageLayout layout, PooledBuffer[] slabs, long capacity)
    {
        Owner = owner;
        Layout = layout;
        _slabs = slabs;
        _slabArrays = new byte[slabs.Length][];
        for (var i = 0; i < slabs.Length; i++)
        {
            _slabArrays[i] = slabs[i].RawBuffer;
        }

        _rowsPerSlab = layout.RowsPerSlab;
        _stride = (int)Math.Min(layout.Stride, int.MaxValue); // only used when a slab holds several rows, where it fits
        RowLength = (int)layout.RowLength;
        CapacityBytes = capacity;
        Id = Interlocked.Increment(ref s_nextId);
    }

    /// <summary>Gets a unique, monotonically increasing identifier. Multi-storage leases are acquired in increasing <see cref="Id"/> order.</summary>
    public long Id { get; }

    public StorageOwner Owner { get; }

    public PixelStorageLayout Layout { get; }

    public int Width => Layout.Width;

    public int Height => Layout.Height;

    public int BytesPerPixel => Layout.BytesPerPixel;

    /// <summary>Gets the number of visible bytes per row.</summary>
    public int RowLength { get; }

    /// <summary>Gets the bytes charged for the slabs (sum of their actual rented capacities).</summary>
    public long CapacityBytes { get; }

    public int SlabCount => _slabs.Length;

    public bool IsDisposed => Volatile.Read(ref _disposed);

    /// <summary>Gets a value indicating whether a lease is active (best effort).</summary>
    public bool IsLeased => LeaseToken != 0;

    /// <summary>Gets or sets the token of the active lease, or 0. Written under the owner lock; read without it for best-effort checks.</summary>
    internal long LeaseToken
    {
        get => Volatile.Read(ref _leaseToken);
        set => Volatile.Write(ref _leaseToken, value);
    }

    internal bool IsDisposedUnsafe => _disposed;

    /// <summary>Acquires the exclusive lease giving access to the rows. Dispose it (in a <see langword="finally"/>, typically with <see langword="using"/>).</summary>
    /// <exception cref="InvalidOperationException">A lease is already active on this storage.</exception>
    /// <exception cref="ObjectDisposedException">The storage or its owner is disposed.</exception>
    public PixelLease AcquireLease() => Owner.AcquireLease(this);

    /// <summary>Releases the slabs. Idempotent.</summary>
    /// <exception cref="InvalidOperationException">A lease is active on this storage; nothing is released.</exception>
    public void Dispose() => Owner.DisposeStorage(this);

    /// <summary>Gets the rented capacity of each slab, for diagnostics.</summary>
    internal int[] GetSlabCapacities()
    {
        var result = new int[_slabs.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = _slabs[i].Capacity;
        }

        return result;
    }

    /// <summary>Returns the index of the slab holding row <paramref name="y"/>.</summary>
    internal int GetSlabIndex(int y) => y / _rowsPerSlab;

    internal Span<byte> GetRowSpanCore(int y)
    {
        if ((uint)y >= (uint)Layout.Height)
            ThrowRowOutOfRange(y);

        var slab = y / _rowsPerSlab;
        var rowInSlab = y - (slab * _rowsPerSlab);
        return new Span<byte>(_slabArrays[slab], rowInSlab * _stride, RowLength);
    }

    internal static PixelStorage Create(StorageOwner owner, PixelStorageLayout layout, AllocationKind kind, AllocationReservation? reservation)
    {
        var scope = owner.Scope;
        if (!layout.TryGetCapacity(scope.Pool, out var capacity))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        // Charge the whole storage before renting anything: an over-limit request rents nothing
        AllocationReservation? ownReservation = null;
        if (reservation is null || reservation.RemainingBytes < capacity)
        {
            ownReservation = scope.Reserve(capacity, kind);
            reservation = ownReservation;
        }

        var slabs = new PooledBuffer[layout.SlabCount];
        var rented = 0;
        try
        {
            if (layout.MaxSlabLength > CheckedSizes.MaxBufferLength)
                throw new ImageResourceLimitException(ImageResourceLimitKind.Width, CheckedSizes.MaxBufferLength / layout.BytesPerPixel, layout.Width);

            var fullLength = (int)layout.FullSlabLength;
            for (; rented < slabs.Length - 1; rented++)
            {
                slabs[rented] = reservation.Rent(fullLength, clear: true);
            }

            slabs[rented] = reservation.Rent((int)layout.LastSlabLength, clear: true);
            rented++;
            return new PixelStorage(owner, layout, slabs, capacity);
        }
        catch
        {
            for (var i = 0; i < rented; i++)
            {
                slabs[i].Dispose();
            }

            throw;
        }
        finally
        {
            ownReservation?.Dispose();
        }
    }

    /// <summary>Marks the storage disposed. Called under the owner lock.</summary>
    internal void MarkDisposed() => Volatile.Write(ref _disposed, value: true);

    /// <summary>Returns the slabs to the pool. Called once, after <see cref="MarkDisposed"/>, when no lease is active.</summary>
    internal void ReleaseSlabs()
    {
        foreach (var slab in _slabs)
        {
            slab.Dispose();
        }
    }

    [DoesNotReturn]
    private static void ThrowRowOutOfRange(int y)
        => throw new ArgumentOutOfRangeException(nameof(y), y, "The row index is out of range.");
}
