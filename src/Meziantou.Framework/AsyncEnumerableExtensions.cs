
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

public static class AsyncEnumerableExtensions
{
    public static IAsyncEnumerable<T> WhereNotNull<T>(this IAsyncEnumerable<T?> enumerable) where T : class
    {
        return enumerable.Where(item => item is not null)!;
    }

    public static IAsyncEnumerable<string> WhereNotNullOrEmpty(this IAsyncEnumerable<string?> source)
    {
        return source.Where(item => !string.IsNullOrEmpty(item))!;
    }

    public static IAsyncEnumerable<string> WhereNotNullOrWhiteSpace(this IAsyncEnumerable<string?> source)
    {
        return source.Where(item => !string.IsNullOrWhiteSpace(item))!;
    }
}
