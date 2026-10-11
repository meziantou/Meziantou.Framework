namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Leases on any number of storages (for example every frame and the poster of an image during a geometry transaction, or
/// the frames of several images). Distinct storages are leased once each, in increasing <see cref="PixelStorage.Id"/> order;
/// repeated storages (aliasing) share one lease, reported by <see cref="AreAliased(int, int)"/>. If an acquisition fails,
/// the leases already acquired are released in reverse order before the exception propagates.
/// </summary>
[SuppressMessage("Design", "MA0182:Unused internal type", Justification = "Shared storage infrastructure; consumed by the image model and the codecs and covered by unit tests.")]
internal ref struct PixelLeaseSet
{
    private readonly PixelStorage[] _distinct;
    private readonly long[] _tokens;
    private readonly int[] _map;
    private int _acquired;

    private PixelLeaseSet(PixelStorage[] distinct, long[] tokens, int[] map, int acquired)
    {
        _distinct = distinct;
        _tokens = tokens;
        _map = map;
        _acquired = acquired;
    }

    /// <summary>Gets the number of storages requested (including repetitions).</summary>
    public readonly int Count => _map?.Length ?? 0;

    /// <summary>Gets the number of distinct leased storages.</summary>
    public readonly int DistinctCount => _distinct?.Length ?? 0;

    /// <summary>Gets the lease for the storage at <paramref name="index"/> in the requested order. Do not dispose it; dispose the set.</summary>
    public readonly PixelLease this[int index]
    {
        get
        {
            var slot = _map[index];
            return new PixelLease(_distinct[slot], _tokens[slot]);
        }
    }

    /// <summary>Acquires leases on the storages.</summary>
    /// <exception cref="InvalidOperationException">A lease is already active on one of the storages.</exception>
    /// <exception cref="ObjectDisposedException">A storage or its owner is disposed.</exception>
    public static PixelLeaseSet Acquire(ReadOnlySpan<PixelStorage> storages)
    {
        var sorted = storages.ToArray();
        foreach (var storage in sorted)
        {
            ArgumentNullException.ThrowIfNull(storage, nameof(storages));
        }

        Array.Sort(sorted, static (left, right) => left.Id.CompareTo(right.Id));
        var distinctCount = 0;
        for (var i = 0; i < sorted.Length; i++)
        {
            if (distinctCount == 0 || !ReferenceEquals(sorted[distinctCount - 1], sorted[i]))
            {
                sorted[distinctCount++] = sorted[i];
            }
        }

        var distinct = sorted.AsSpan(0, distinctCount).ToArray();
        var map = new int[storages.Length];
        for (var i = 0; i < storages.Length; i++)
        {
            map[i] = Array.BinarySearch(distinct, storages[i], IdComparer.Instance);
        }

        var tokens = new long[distinctCount];
        var acquired = 0;
        try
        {
            for (; acquired < distinctCount; acquired++)
            {
                tokens[acquired] = distinct[acquired].AcquireLease().Token;
            }
        }
        catch
        {
            new PixelLeaseSet(distinct, tokens, map, acquired).Dispose();
            throw;
        }

        return new PixelLeaseSet(distinct, tokens, map, acquired);
    }

    /// <summary>Returns <see langword="true"/> if the storages at the two requested indices are the same storage.</summary>
    public readonly bool AreAliased(int first, int second) => _map[first] == _map[second];

    public void Dispose()
    {
        for (var i = _acquired - 1; i >= 0; i--)
        {
            var storage = _distinct[i];
            storage.Owner.ReleaseLease(storage, _tokens[i]);
        }

        _acquired = 0;
    }

    private sealed class IdComparer : IComparer<PixelStorage>
    {
        public static IdComparer Instance { get; } = new();

        public int Compare(PixelStorage? x, PixelStorage? y) => x!.Id.CompareTo(y!.Id);
    }
}
