namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Budgets and allocates the replacement storages of a transactional geometry operation (crop, auto-crop, resize, rotate,
/// auto-orient). The replacements are charged to the same scope while the original
/// storages are still live, so the operation needs room for both at once. Any failure before <see cref="Commit"/> (limit,
/// allocation, cancellation, callback exception) releases every replacement and leaves the original storages untouched.
/// </summary>
/// <remarks>
/// <para>Usage:</para>
/// <code>
/// using var transaction = new PixelStorageTransaction(owner, "resize the image");
/// transaction.Reserve(frameCount, newWidth, newHeight, bytesPerPixel); // optional: fail fast for the whole operation
/// var replacement = transaction.Allocate(newWidth, newHeight, bytesPerPixel); // per frame; fill under a lease pair
/// transaction.Commit(); // throws if the image was disposed or leased meanwhile; nothing is changed then
/// // swap the storage references (no-throw code), then dispose the original storages
/// </code>
/// </remarks>
internal sealed class PixelStorageTransaction : IDisposable
{
    private readonly StorageOwner _owner;
    private readonly string _operation;
    private readonly List<PixelStorage> _allocated = [];
    private AllocationReservation? _reservation;
    private bool _completed;

    /// <exception cref="ObjectDisposedException">The owner is disposed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease of the owner is active.</exception>
    public PixelStorageTransaction(StorageOwner owner, string operation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(operation);
        owner.EnsureCanModify(operation);
        _owner = owner;
        _operation = operation;
    }

    /// <summary>Gets the storages allocated so far.</summary>
    public IReadOnlyList<PixelStorage> Allocated => _allocated;

    /// <summary>Charges the capacity of <paramref name="count"/> storages of the given geometry before allocating any of them.</summary>
    /// <exception cref="ImageResourceLimitException">The replacements and the live storages do not fit in the limit together; nothing is charged.</exception>
    public void Reserve(int count, int width, int height, int bytesPerPixel, PixelStorageLayoutOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ThrowIfCompleted();
        var scope = _owner.Scope;
        var layout = PixelStorageLayout.Create(width, height, bytesPerPixel, options);
        if (!layout.TryGetCapacity(scope.Pool, out var capacity) || !CheckedSizes.TryMultiply(capacity, count, out var total))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var reservation = scope.Reserve(total, AllocationKind.ImagePixels);
        _reservation?.Dispose();
        _reservation = reservation;
    }

    /// <summary>Allocates one zeroed replacement storage, drawing on the reservation when one covers it.</summary>
    /// <exception cref="ImageResourceLimitException">The storage would exceed the limit; previously allocated replacements stay owned by the transaction.</exception>
    public PixelStorage Allocate(int width, int height, int bytesPerPixel, PixelStorageLayoutOptions? options = null)
    {
        ThrowIfCompleted();
        var storage = _owner.Allocate(PixelStorageLayout.Create(width, height, bytesPerPixel, options), AllocationKind.ImagePixels, _reservation);
        _allocated.Add(storage);
        return storage;
    }

    /// <summary>
    /// Validates that the replacements can be published and transfers their ownership to the caller, which must then swap
    /// the storage references and dispose the originals without any further failure point.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The owner was disposed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease of the owner is active (the replacements must not be leased either).</exception>
    public void Commit()
    {
        ThrowIfCompleted();
        _owner.EnsureCanModify(_operation);
        _completed = true;
        _reservation?.Dispose();
        _reservation = null;
    }

    /// <summary>Releases the unused reservation and, unless committed, every replacement storage.</summary>
    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
        if (_completed)
            return;

        _completed = true;
        foreach (var storage in _allocated)
        {
            storage.Dispose();
        }

        _allocated.Clear();
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
            throw new InvalidOperationException("The storage transaction is already completed.");
    }
}
