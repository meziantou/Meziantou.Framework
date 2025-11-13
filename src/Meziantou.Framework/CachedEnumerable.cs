namespace Meziantou.Framework;

/// <summary>Provides factory methods for creating cached enumerables that store enumeration results to prevent multiple enumerations.</summary>
/// <example>
/// <code>
/// var source = GetExpensiveEnumerable();
/// var cached = CachedEnumerable.Create(source);
/// foreach (var item in cached) { } // First enumeration
/// foreach (var item in cached) { } // Uses cached results
/// </code>
/// </example>
public static class CachedEnumerable
{
    /// <summary>Creates a cached enumerable that stores enumeration results.</summary>
    /// <param name="threadSafe">Whether the cached enumerable should be thread-safe.</param>
    public static ICachedEnumerable<T> Create<T>(IEnumerable<T> enumerable, bool threadSafe = true)
    {
        if (threadSafe)
            return new CachedEnumerableThreadSafe<T>(enumerable);

        return new CachedEnumerable<T>(enumerable);
    }

    /// <summary>Creates a cached async enumerable that stores enumeration results.</summary>
    /// <param name="threadSafe">Whether the cached async enumerable should be thread-safe.</param>
    public static ICachedAsyncEnumerable<T> Create<T>(IAsyncEnumerable<T> enumerable, bool threadSafe = true)
    {
        if (threadSafe)
            return new CachedAsyncEnumerableThreadSafe<T>(enumerable);

        return new CachedAsyncEnumerable<T>(enumerable);
    }
}
