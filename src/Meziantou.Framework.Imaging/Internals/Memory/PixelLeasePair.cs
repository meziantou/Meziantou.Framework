namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Leases on two storages for a cross-frame or cross-image operation (copy, compositing). Leases are acquired in increasing
/// <see cref="PixelStorage.Id"/> order regardless of the argument order, and released in reverse order. When both arguments
/// are the same storage (aliasing), a single lease is acquired and <see cref="IsAliased"/> is <see langword="true"/>: the
/// operation must then handle in-place processing explicitly.
/// </summary>
internal ref struct PixelLeasePair
{
    private PixelLease _lower;
    private PixelLease _higher;
    private readonly bool _firstIsLower;

    private PixelLeasePair(PixelLease lower, PixelLease higher, bool firstIsLower, bool isAliased)
    {
        _lower = lower;
        _higher = higher;
        _firstIsLower = firstIsLower;
        IsAliased = isAliased;
    }

    /// <summary>Gets a value indicating whether both leases designate the same storage.</summary>
    public readonly bool IsAliased { get; }

    /// <summary>Gets the lease of the first storage. Do not dispose it; dispose the pair.</summary>
    public readonly PixelLease First => IsAliased || _firstIsLower ? _lower : _higher;

    /// <summary>Gets the lease of the second storage. Do not dispose it; dispose the pair.</summary>
    public readonly PixelLease Second => IsAliased ? _lower : _firstIsLower ? _higher : _lower;

    /// <summary>Acquires leases on two storages in stable order. If the second acquisition fails, the first lease is released.</summary>
    /// <exception cref="InvalidOperationException">A lease is already active on one of the storages.</exception>
    /// <exception cref="ObjectDisposedException">A storage or its owner is disposed.</exception>
    public static PixelLeasePair Acquire(PixelStorage first, PixelStorage second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (ReferenceEquals(first, second))
            return new PixelLeasePair(first.AcquireLease(), higher: default, firstIsLower: true, isAliased: true);

        var firstIsLower = first.Id < second.Id;
        var lowerStorage = firstIsLower ? first : second;
        var higherStorage = firstIsLower ? second : first;
        var lower = lowerStorage.AcquireLease();
        try
        {
            var higher = higherStorage.AcquireLease();
            return new PixelLeasePair(lower, higher, firstIsLower, isAliased: false);
        }
        catch
        {
            lower.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _higher.Dispose();
        _lower.Dispose();
    }
}
