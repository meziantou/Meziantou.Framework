namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Library-controlled bytes that are not rented from the pool (for example retained metadata arrays) charged to an
/// <see cref="AllocationScope"/>. Disposal releases the charge exactly once (idempotent and thread-safe).
/// </summary>
internal sealed class AllocationCharge : IDisposable
{
    private int _released;

    internal AllocationCharge(AllocationScope scope, AllocationKind kind, long bytes)
    {
        Scope = scope;
        Kind = kind;
        Bytes = bytes;
    }

    public AllocationScope Scope { get; }

    public AllocationKind Kind { get; }

    public long Bytes { get; }

    public bool IsReleased => Volatile.Read(ref _released) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            Scope.ReleaseAllocation(Bytes, Kind);
        }
    }
}
