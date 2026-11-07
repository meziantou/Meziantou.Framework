namespace Meziantou.Framework.Fonts;

/// <summary>
/// The characters of the faces of a <see cref="SystemFontCollection"/>, read from their files the first time they are
/// asked for and kept within a number of bytes: when the coverages that are kept would take more, the ones read first
/// are dropped, and read again when they are asked for again.
/// </summary>
internal sealed class SystemFontCoverageCache
{
    /// <summary>The bytes the coverages of a collection keep by default: far more than the fonts of a machine need.</summary>
    public const long DefaultBudget = 16L * 1024 * 1024;

    private readonly Queue<SystemFontDescriptor> _loaded = new();
    private readonly Lock _lock = new();
    private readonly long _budget;
    private long _size;
    private int _readCount;

    public SystemFontCoverageCache(long budget)
    {
        _budget = budget;
    }

    /// <summary>Gets the cache of the descriptors that belong to no collection.</summary>
    public static SystemFontCoverageCache Shared { get; } = new(DefaultBudget);

    /// <summary>Gets the number of character maps that were read from the files.</summary>
    public int ReadCount => Volatile.Read(ref _readCount);

    /// <summary>Gets the number of bytes the coverages that are kept take.</summary>
    public long Size
    {
        get
        {
            lock (_lock)
            {
                return _size;
            }
        }
    }

    public SystemFontCoverage Get(SystemFontDescriptor descriptor)
    {
        if (descriptor.Coverage is { } coverage)
            return coverage;

        // Reading the file is done outside the lock: two threads may read the same face, and the first one is kept.
        coverage = SystemFontScanner.ReadCoverage(descriptor.FilePath, descriptor.FaceIndex);
        Interlocked.Increment(ref _readCount);
        lock (_lock)
        {
            if (descriptor.Coverage is { } existing)
                return existing;

            descriptor.Coverage = coverage;
            _loaded.Enqueue(descriptor);
            _size += coverage.Size;

            // The coverage that was just read is kept, even when it alone is larger than the budget.
            while (_size > _budget && _loaded.Count > 1)
            {
                var evicted = _loaded.Dequeue();
                _size -= evicted.Coverage!.Size;
                evicted.Coverage = null;
            }
        }

        return coverage;
    }
}
