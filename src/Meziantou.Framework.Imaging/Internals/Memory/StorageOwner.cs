namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The lifetime and lease gate of a group of <see cref="PixelStorage"/> objects owned by one image (its frames and poster)
/// or by one reader/writer (its working state).
/// </summary>
/// <remarks>
/// <para>
/// Pixel access goes through exclusive scoped leases (<see cref="PixelStorage.AcquireLease"/>). While any lease of the owner
/// is active, disposal, structural edits and storage replacement (<see cref="EnsureCanModify(string)"/>) throw
/// <see cref="InvalidOperationException"/>, and a second lease on the same storage is rejected (reentrant or concurrent
/// access). Memory that is visible to a lease holder is therefore never returned to the pool.
/// </para>
/// <para>
/// Detection is best-effort and is not a synchronization mechanism; the state transitions themselves are atomic, so a
/// concurrent disposal either fails or completes, and disposal is idempotent. Disposing the owner releases every storage it
/// still holds. Storages are charged to <see cref="Scope"/>, which may outlive the owner (reader scopes) or be shared with
/// other owners.
/// </para>
/// </remarks>
internal sealed class StorageOwner
{
    private static long s_nextLeaseToken;

    private readonly Lock _lock = new();
    private readonly HashSet<PixelStorage> _storages = [];
    private bool _disposed;
    private int _activeLeases;

    public StorageOwner(AllocationScope scope, string objectName = "Image")
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(objectName);
        Scope = scope;
        ObjectName = objectName;
    }

    public AllocationScope Scope { get; }

    /// <summary>Gets the object name reported by <see cref="ObjectDisposedException"/>.</summary>
    public string ObjectName { get; }

    public bool IsDisposed
    {
        get
        {
            lock (_lock)
            {
                return _disposed;
            }
        }
    }

    /// <summary>Gets the number of active leases on the storages of this owner.</summary>
    public int ActiveLeaseCount
    {
        get
        {
            lock (_lock)
            {
                return _activeLeases;
            }
        }
    }

    /// <summary>Gets the number of live (not disposed) storages of this owner.</summary>
    public int LiveStorageCount
    {
        get
        {
            lock (_lock)
            {
                return _storages.Count;
            }
        }
    }

    /// <summary>Allocates zeroed pixel storage charged to <see cref="Scope"/>.</summary>
    /// <exception cref="ImageResourceLimitException">The storage would exceed the live-allocation limit; nothing is allocated.</exception>
    /// <exception cref="OutOfMemoryException">A slab cannot be allocated; every slab already rented is released.</exception>
    /// <exception cref="ObjectDisposedException">The owner is disposed.</exception>
    public PixelStorage Allocate(int width, int height, int bytesPerPixel, PixelStorageLayoutOptions? options = null, AllocationKind kind = AllocationKind.ImagePixels)
        => Allocate(PixelStorageLayout.Create(width, height, bytesPerPixel, options), kind, reservation: null);

    /// <summary>Allocates zeroed pixel storage, drawing on <paramref name="reservation"/> when it covers the storage.</summary>
    internal PixelStorage Allocate(PixelStorageLayout layout, AllocationKind kind, AllocationReservation? reservation)
    {
        ThrowIfDisposed();
        var storage = PixelStorage.Create(this, layout, kind, reservation);
        lock (_lock)
        {
            if (!_disposed)
            {
                _storages.Add(storage);
                return storage;
            }

            storage.MarkDisposed();
        }

        storage.ReleaseSlabs();
        throw new ObjectDisposedException(ObjectName);
    }

    /// <exception cref="ObjectDisposedException">The owner is disposed.</exception>
    public void ThrowIfDisposed()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, ObjectName);
        }
    }

    /// <summary>Validates that a structural edit or storage replacement may proceed.</summary>
    /// <param name="operation">The operation, for the exception message (for example "remove a frame").</param>
    /// <exception cref="ObjectDisposedException">The owner is disposed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    public void EnsureCanModify(string operation)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, ObjectName);
            if (_activeLeases > 0)
                throw CreateLeaseActiveException(operation);
        }
    }

    /// <summary>Disposes the owner and releases every storage it holds.</summary>
    /// <returns><see langword="true"/> if this call disposed the owner; <see langword="false"/> if it was already disposed.</returns>
    /// <exception cref="InvalidOperationException">A pixel lease is active (reentrant disposal from a callback, or concurrent use); nothing is released.</exception>
    public bool Dispose()
    {
        PixelStorage[] storages;
        lock (_lock)
        {
            if (_disposed)
                return false;

            if (_activeLeases > 0)
                throw CreateLeaseActiveException("dispose");

            _disposed = true;
            storages = [.. _storages];
            _storages.Clear();
            foreach (var storage in storages)
            {
                storage.MarkDisposed();
            }
        }

        foreach (var storage in storages)
        {
            storage.ReleaseSlabs();
        }

        return true;
    }

    internal void DisposeStorage(PixelStorage storage)
    {
        lock (_lock)
        {
            if (storage.IsDisposedUnsafe)
                return;

            if (storage.LeaseToken != 0)
                throw CreateLeaseActiveException("release the pixel storage");

            storage.MarkDisposed();
            _storages.Remove(storage);
        }

        storage.ReleaseSlabs();
    }

    internal PixelLease AcquireLease(PixelStorage storage)
    {
        long token;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed || storage.IsDisposedUnsafe, ObjectName);
            if (storage.LeaseToken != 0)
                throw new InvalidOperationException("A pixel lease is already active on this frame. Reentrant or concurrent pixel access is not allowed.");

            token = Interlocked.Increment(ref s_nextLeaseToken);
            storage.LeaseToken = token;
            _activeLeases++;
        }

        return new PixelLease(storage, token);
    }

    internal void ReleaseLease(PixelStorage storage, long token)
    {
        lock (_lock)
        {
            if (storage.LeaseToken != token)
                return;

            storage.LeaseToken = 0;
            _activeLeases--;
        }
    }

    private static InvalidOperationException CreateLeaseActiveException(string operation)
        => new($"Cannot {operation} while a pixel lease is active (for example from inside a pixel callback).");
}
