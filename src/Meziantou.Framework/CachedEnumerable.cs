namespace Meziantou.Framework;

public static class CachedEnumerable
{
    public static ICachedEnumerable<T> Create<T>(IEnumerable<T> enumerable, bool threadSafe = true)
    {
        if (threadSafe)
            return new CachedEnumerableThreadSafe<T>(enumerable);

        return new CachedEnumerable<T>(enumerable);
    }

    public static ICachedAsyncEnumerable<T> Create<T>(IAsyncEnumerable<T> enumerable, bool threadSafe = true)
    {
        if (threadSafe)
            return new CachedAsyncEnumerableThreadSafe<T>(enumerable);

        return new CachedAsyncEnumerable<T>(enumerable);
    }
}
