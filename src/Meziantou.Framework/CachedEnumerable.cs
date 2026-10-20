namespace Meziantou.Framework;

/// <summary>Provides factory methods for creating cached enumerables that store enumeration results to prevent multiple enumerations.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/CachedEnumerableTests.cs" ref:region="CachedEnumerable_Create" ref:dedent="true">
/// var enumerationCount = 0;
/// IEnumerable&lt;int&gt; GetExpensiveEnumerable()
/// {
///     enumerationCount++;
///     yield return 1;
///     yield return 2;
/// }
///
/// using var cached = CachedEnumerable.Create(GetExpensiveEnumerable());
/// Assert.Equal([1, 2], cached); // First enumeration
/// Assert.Equal([1, 2], cached); // Uses cached results
/// Assert.Equal(1, enumerationCount);
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
