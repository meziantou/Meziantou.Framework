using System.Text;

namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>
/// A <see cref="StringBuilder"/> reused by the hot paths of the current thread that build short strings (e.g. the URL parser
/// and serializer): growing a new builder for each string allocates its chunks again. A builder that is not released (e.g.
/// when a nested call acquires a builder) is collected.
/// </summary>
internal static class StringBuilderCache
{
    // The builders that grow larger are not kept, so the cache does not hold a large buffer
    private const int MaxCapacity = 1024;

    [ThreadStatic]
    private static StringBuilder? s_cachedInstance;

    public static StringBuilder Acquire(int capacity = 16)
    {
        if (capacity <= MaxCapacity && s_cachedInstance is { } builder && capacity <= builder.Capacity)
        {
            s_cachedInstance = null;
            builder.Clear();
            return builder;
        }

        return new StringBuilder(capacity);
    }

    public static void Release(StringBuilder builder)
    {
        if (builder.Capacity <= MaxCapacity)
        {
            s_cachedInstance = builder;
        }
    }

    /// <summary>Returns the content of the builder and releases it.</summary>
    public static string GetStringAndRelease(StringBuilder builder)
    {
        var result = builder.ToString();
        Release(builder);
        return result;
    }
}
