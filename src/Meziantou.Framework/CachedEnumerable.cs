namespace Meziantou.Framework;

/// <summary>
/// Provides factory methods for creating cached enumerables.
/// </summary>
public static class CachedEnumerable
{
    /// <summary>
    /// Creates a cached enumerable that stores values from the source enumerable for reuse across multiple enumerations.
    /// </summary>
    /// <typeparam name="T">The type of elements in the enumerable.</typeparam>
    /// <param name="enumerable">The source enumerable to cache.</param>
    /// <param name="threadSafe">If <see langword="true"/>, the cached enumerable will be thread-safe; otherwise, <see langword="false"/>.</param>
    /// <returns>A cached enumerable.</returns>
    public static ICachedEnumerable<T> Create<T>(IEnumerable<T> enumerable, bool threadSafe = true)
    {
        if (threadSafe)
            return new CachedEnumerableThreadSafe<T>(enumerable);

        return new CachedEnumerable<T>(enumerable);
    }

    /// <summary>
    /// Creates a cached asynchronous enumerable that stores values from the source enumerable for reuse across multiple enumerations.
    /// </summary>
    /// <typeparam name="T">The type of elements in the enumerable.</typeparam>
    /// <param name="enumerable">The source asynchronous enumerable to cache.</param>
    /// <param name="threadSafe">If <see langword="true"/>, the cached enumerable will be thread-safe; otherwise, <see langword="false"/>.</param>
    /// <returns>A cached asynchronous enumerable.</returns>
    public static ICachedAsyncEnumerable<T> Create<T>(IAsyncEnumerable<T> enumerable, bool threadSafe = true)
    {
        if (threadSafe)
            return new CachedAsyncEnumerableThreadSafe<T>(enumerable);

        return new CachedAsyncEnumerable<T>(enumerable);
    }
}
