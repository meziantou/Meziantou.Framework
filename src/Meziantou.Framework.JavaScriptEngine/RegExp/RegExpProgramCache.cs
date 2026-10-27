using System.Collections.Concurrent;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A cache of compiled patterns, keyed by the pattern and the flags that change the matcher. It is cleared entirely
/// when it is full, so a pattern compiled again later can get a new <see cref="RegExpProgram"/>.
/// </summary>
internal sealed class RegExpProgramCache
{
    private readonly ConcurrentDictionary<(string Pattern, RegExpFlags Flags), RegExpProgram> _cache = new();
    private readonly int _maxSize;

    // ConcurrentDictionary.Count takes all the locks of the dictionary: the number of entries is tracked separately
    private int _count;

    public RegExpProgramCache(int maxSize)
    {
        _maxSize = maxSize;
    }

    /// <summary>Gets the process-wide cache used by the RegExp constructor.</summary>
    public static RegExpProgramCache Shared { get; } = new(maxSize: 2048);

    /// <summary>Compiles a pattern, or gets it from the cache.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags; only the flags that change the matcher are part of the key.</param>
    /// <param name="error">The message of the SyntaxError when the pattern is not valid.</param>
    public RegExpProgram? GetOrCompile(string pattern, RegExpFlags flags, out string? error)
    {
        // g, y and d do not change the matcher
        var key = (pattern, flags & ~(RegExpFlags.Global | RegExpFlags.Sticky | RegExpFlags.HasIndices));
        if (_cache.TryGetValue(key, out var program))
        {
            error = null;
            return program;
        }

        program = RegExpProgram.Compile(pattern, key.Item2, out error);
        if (program is null)
            return null;

        if (Interlocked.Increment(ref _count) > _maxSize)
        {
            Interlocked.Exchange(ref _count, 0);
            _cache.Clear();
        }

        // Another thread may have cached the same pattern meanwhile: return its instance so that all callers share it
        return _cache.GetOrAdd(key, program);
    }
}
