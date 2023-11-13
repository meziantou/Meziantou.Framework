using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

public static class AsyncEnumerableExtensions
{
    public static async ValueTask<bool> AnyAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        await foreach (var _ in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    public static async ValueTask<bool> AnyAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
                return true;
        }

        return false;
    }

    public static async IAsyncEnumerable<T> ConcatAsync<T>(this IAsyncEnumerable<T> first, IAsyncEnumerable<T> second, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in first.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return item;

        await foreach (var item in second.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public static ValueTask<bool> ContainsAsync<T>(this IAsyncEnumerable<T> enumerable, T value, CancellationToken cancellationToken = default)
    {
        return ContainsAsync(enumerable, value, comparer: null, cancellationToken);
    }

    public static async ValueTask<bool> ContainsAsync<T>(this IAsyncEnumerable<T> enumerable, T value, IEqualityComparer<T>? comparer, CancellationToken cancellationToken = default)
    {
        comparer ??= EqualityComparer<T>.Default;

        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (comparer.Equals(item, value))
            {
                return true;
            }
        }

        return false;
    }

    public static async ValueTask<int> CountAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        var result = 0;
        await foreach (var _ in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            result++;
        }

        return result;
    }

    public static async ValueTask<int> CountAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        var result = 0;
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
            {
                result++;
            }
        }

        return result;
    }

    public static IAsyncEnumerable<T> DistinctAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        return DistinctAsync(enumerable, comparer: null, cancellationToken);
    }

    public static async IAsyncEnumerable<T> DistinctAsync<T>(this IAsyncEnumerable<T> enumerable, IEqualityComparer<T>? comparer, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hashSet = new HashSet<T>(comparer);
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (hashSet.Add(item))
                yield return item;
        }
    }

    public static IAsyncEnumerable<T> DistinctByAsync<T, TKey>(this IAsyncEnumerable<T> enumerable, Func<T, TKey> getKey, CancellationToken cancellationToken = default)
    {
        return DistinctByAsync(enumerable, getKey, comparer: null, cancellationToken);
    }

    public static async IAsyncEnumerable<T> DistinctByAsync<T, TKey>(this IAsyncEnumerable<T> enumerable, Func<T, TKey> getKey, IEqualityComparer<TKey>? comparer, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hashSet = new HashSet<TKey>(comparer);
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var key = getKey(item);
            if (hashSet.Add(key))
                yield return item;
        }
    }

    public static ValueTask<T> FirstAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        return FirstAsync(enumerable, _ => true, cancellationToken);
    }

    public static async ValueTask<T> FirstAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
                return item;
        }

        throw new InvalidOperationException("The source sequence is empty");
    }

    public static ValueTask<T?> FirstOrDefaultAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        return FirstOrDefaultAsync(enumerable, _ => true, cancellationToken);
    }

    public static async ValueTask<T?> FirstOrDefaultAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
                return item;
        }

        return default;
    }

    public static ValueTask<T> LastAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        return LastAsync(enumerable, _ => true, cancellationToken);
    }

    public static async ValueTask<T> LastAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        var hasValue = false;
        T result = default!;
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
            {
                hasValue = true;
                result = item;
            }
        }

        if (hasValue)
            return result!;

        throw new InvalidOperationException("The source sequence is empty");
    }

    public static ValueTask<T?> LastOrDefaultAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        return LastOrDefaultAsync(enumerable, _ => true, cancellationToken);
    }

    public static async ValueTask<T?> LastOrDefaultAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        T? result = default;
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
            {
                result = item;
            }
        }

        return result;
    }

    public static async ValueTask<long> LongCountAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        var result = 0L;
        await foreach (var _ in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            result++;
        }

        return result;
    }

    public static async ValueTask<long> LongCountAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> predicate, CancellationToken cancellationToken = default)
    {
        var result = 0L;
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
            {
                result++;
            }
        }

        return result;
    }

    public static async IAsyncEnumerable<TResult> OfTypeAsync<T, TResult>(this IAsyncEnumerable<T> enumerable, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item is TResult result)
                yield return result;
        }
    }

    public static async IAsyncEnumerable<TResult> SelectAsync<T, TResult>(this IAsyncEnumerable<T> enumerable, Func<T, TResult> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return selector(item);
        }
    }

    public static async IAsyncEnumerable<T> TakeAsync<T>(this IAsyncEnumerable<T> enumerable, int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            yield break;

        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
            if (--count == 0)
                yield break;
        }
    }

    public static async IAsyncEnumerable<T> TakeWhileAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!selector(item))
                yield break;

            yield return item;
        }
    }

    public static async IAsyncEnumerable<T> SkipAsync<T>(this IAsyncEnumerable<T> enumerable, int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T>? enumerator = null;
        try
        {
            enumerator = enumerable.GetAsyncEnumerator(cancellationToken);
            if (count > 0)
            {
                while (count > 0 && await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    count--;
                }

                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield return enumerator.Current;
                }
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public static async IAsyncEnumerable<T> SkipWhileAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (selector(item))
                continue;

            yield return item;
        }
    }

    [SuppressMessage("Design", "MA0016:Prefer return collection abstraction instead of implementation", Justification = "Match Enumerable.ToList signature")]
    public static async ValueTask<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        var list = new List<T>();
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(item);
        }

        return list;
    }

    public static async IAsyncEnumerable<T> WhereAsync<T>(this IAsyncEnumerable<T> enumerable, Func<T, bool> selector, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (selector(item))
                yield return item;
        }
    }

    public static IAsyncEnumerable<T> WhereNotNull<T>(this IAsyncEnumerable<T?> enumerable, CancellationToken cancellationToken = default) where T : class
    {
        return enumerable.WhereAsync(item => item is not null, cancellationToken)!;
    }

    public static IAsyncEnumerable<string> WhereNotNullOrEmpty(this IAsyncEnumerable<string?> source, CancellationToken cancellationToken = default)
    {
        return source.WhereAsync(item => !string.IsNullOrEmpty(item), cancellationToken)!;
    }

    public static IAsyncEnumerable<string> WhereNotNullOrWhiteSpace(this IAsyncEnumerable<string?> source, CancellationToken cancellationToken = default)
    {
        return source.WhereAsync(item => !string.IsNullOrWhiteSpace(item), cancellationToken)!;
    }
}
