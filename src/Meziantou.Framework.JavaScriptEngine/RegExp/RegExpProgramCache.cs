using System.Collections.Concurrent;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A cache of compiled patterns, keyed by the pattern and the flags that change the matcher. It is bounded by the number
/// of entries and by their total weight (the size of the compiled programs). When a bound is exceeded, a sweep evicts
/// the entries not used since the previous sweep (a second-chance policy), so the patterns in use stay compiled. A pattern
/// compiled again after its eviction can get a new <see cref="RegExpProgram"/>.
/// </summary>
internal sealed class RegExpProgramCache
{
    private readonly ConcurrentDictionary<(string Pattern, RegExpFlags Flags), Entry> _cache = new();
    private readonly Lock _sweepLock = new();
    private readonly int _maxSize;
    private readonly long _maxWeight;

    // ConcurrentDictionary.Count takes all the locks of the dictionary: the number of entries and their weight are
    // tracked separately
    private int _count;
    private long _weight;

    public RegExpProgramCache(int maxSize, long maxWeight = long.MaxValue)
    {
        _maxSize = maxSize;
        _maxWeight = maxWeight;
    }

    /// <summary>Gets the process-wide cache used by the RegExp constructor.</summary>
    public static RegExpProgramCache Shared { get; } = new(maxSize: 2048, maxWeight: 4 * 1024 * 1024);

    /// <summary>Gets the number of cached patterns.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Compiles a pattern, or gets it from the cache.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags; only the flags that change the matcher are part of the key.</param>
    /// <param name="error">The message of the SyntaxError when the pattern is not valid.</param>
    public RegExpProgram? GetOrCompile(string pattern, RegExpFlags flags, out string? error)
    {
        // g, y and d do not change the matcher
        var key = (pattern, flags & ~(RegExpFlags.Global | RegExpFlags.Sticky | RegExpFlags.HasIndices));
        if (_cache.TryGetValue(key, out var entry))
        {
            // Write only when needed: a hot entry is read by all the threads
            if (!entry.Used)
            {
                entry.Used = true;
            }

            error = null;
            return entry.Program;
        }

        var program = RegExpProgram.Compile(pattern, key.Item2, out error);
        if (program is null)
            return null;

        var weight = GetWeight(program);
        if (weight > _maxWeight)
            return program; // too large to be cached

        var newEntry = new Entry(program, weight);
        entry = _cache.GetOrAdd(key, newEntry);
        if (!ReferenceEquals(entry, newEntry))
        {
            // Another thread cached the same pattern meanwhile: return its instance so that all callers share it
            return entry.Program;
        }

        var count = Interlocked.Increment(ref _count);
        var totalWeight = Interlocked.Add(ref _weight, weight);
        if (count > _maxSize || totalWeight > _maxWeight)
        {
            Sweep();
        }

        return program;
    }

    /// <summary>An estimate of the memory of a program, in units of 4 bytes.</summary>
    private static long GetWeight(RegExpProgram program)
    {
        long weight = 64 + program.Source.Length + program.Code.Length + (program.Classes.Length * 16);
        foreach (var value in program.Strings)
        {
            weight += value.Length;
        }

        return weight;
    }

    /// <summary>
    /// Evicts all the entries not used since the previous sweep, and clears the "used" bit of the others; then, when the
    /// cache is still more than 3/4 full (all the entries are in use), evicts entries until it is not.
    /// </summary>
    private void Sweep()
    {
        // One sweep at a time: the other threads add their entry and go on
        if (!_sweepLock.TryEnter())
            return;

        try
        {
            foreach (var (key, entry) in _cache)
            {
                if (entry.Used)
                {
                    entry.Used = false;
                }
                else
                {
                    Remove(key, entry);
                }
            }

            var targetCount = _maxSize - (_maxSize / 4);
            var targetWeight = _maxWeight - (_maxWeight / 4);
            foreach (var (key, entry) in _cache)
            {
                if (Volatile.Read(ref _count) <= targetCount && Volatile.Read(ref _weight) <= targetWeight)
                    break;

                Remove(key, entry);
            }
        }
        finally
        {
            _sweepLock.Exit();
        }
    }

    private void Remove((string Pattern, RegExpFlags Flags) key, Entry entry)
    {
        if (_cache.TryRemove(new KeyValuePair<(string Pattern, RegExpFlags Flags), Entry>(key, entry)))
        {
            Interlocked.Decrement(ref _count);
            Interlocked.Add(ref _weight, -entry.Weight);
        }
    }

    private sealed class Entry(RegExpProgram program, long weight)
    {
        public RegExpProgram Program { get; } = program;

        public long Weight { get; } = weight;

        private volatile bool _used;

        // Set when the entry is read, cleared by the sweeps; a new entry starts unused, so a pattern used once is evicted
        // before the patterns used again
        public bool Used
        {
            get => _used;
            set => _used = value;
        }
    }
}
