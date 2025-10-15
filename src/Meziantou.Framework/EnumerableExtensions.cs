using System.Collections.ObjectModel;

namespace Meziantou.Framework;

public static partial class EnumerableExtensions
{
    /// <summary>
    /// Allow to use the foreach keyword with an IEnumerator
    /// </summary>
    public static IEnumerator<T> GetEnumerator<T>(this IEnumerator<T> enumerator) => enumerator;

    /// <summary>
    /// Allow to use the foreach keyword with an IAsyncEnumerator
    /// </summary>
    public static IAsyncEnumerator<T> GetAsyncEnumerator<T>(this IAsyncEnumerator<T> enumerator) => enumerator;

    public static void AddRange<T>(this ICollection<T> collection, params T[] items)
    {
        ArgumentNullException.ThrowIfNull(collection);

        ArgumentNullException.ThrowIfNull(items);

        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T>? items)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (items is not null)
        {
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }
    }

    public static void RemoveAll<T>(this ICollection<T> collection, Predicate<T> match)
    {
        if (collection is List<T> list)
        {
            list.RemoveAll(match);
        }
        else
        {
            var itemsToRemove = collection.Where(item => match(item)).ToArray();
            foreach (var item in itemsToRemove)
            {
                collection.Remove(item);
            }
        }
    }

    public static void Replace<T>(this IList<T> list, T oldItem, T newItem)
    {
        ArgumentNullException.ThrowIfNull(list);

        var index = list.IndexOf(oldItem);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(oldItem));

        list[index] = newItem;
    }

    public static void AddOrReplace<T>(this IList<T> list, T? oldItem, T newItem)
    {
        ArgumentNullException.ThrowIfNull(list);

        var index = list.IndexOf(oldItem!);
        if (index < 0)
        {
            list.Add(newItem);
        }
        else
        {
            list[index] = newItem;
        }
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> items)
        where T : struct
    {
        foreach (var item in items)
        {
            if (item.HasValue)
                yield return item.GetValueOrDefault();
        }
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Where(item => item is not null)!;
    }

    public static IEnumerable<string> WhereNotNullOrEmpty(this IEnumerable<string?> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Where(item => !string.IsNullOrEmpty(item))!;
    }

    public static IEnumerable<string> WhereNotNullOrWhiteSpace(this IEnumerable<string?> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Where(item => !string.IsNullOrWhiteSpace(item))!;
    }

    public static bool IsDistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(source);

        ArgumentNullException.ThrowIfNull(keySelector);

        return IsDistinctBy(source, keySelector, EqualityComparer<TKey>.Default);
    }

    public static bool IsDistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
    {
        ArgumentNullException.ThrowIfNull(source);

        ArgumentNullException.ThrowIfNull(keySelector);

        var hash = new HashSet<TKey>(comparer);
        foreach (var item in source)
        {
            if (!hash.Add(keySelector(item)))
                return false;
        }

        return true;
    }

    public static bool IsDistinct<TSource>(this IEnumerable<TSource> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return IsDistinct(source, EqualityComparer<TSource>.Default);
    }

    public static bool IsDistinct<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer)
    {
        ArgumentNullException.ThrowIfNull(source);

        var hash = new HashSet<TSource>(comparer);
        foreach (var item in source)
        {
            if (!hash.Add(item))
                return false;
        }

        return true;
    }

#if NET7_0_OR_GREATER
    [Obsolete("Use Order()", DiagnosticId = "MEZ_NET7")]
#endif
    public static IEnumerable<T> Sort<T>(this IEnumerable<T> list)
    {
        return Sort(list, comparer: null);
    }

#if NET7_0_OR_GREATER
    [Obsolete("Use Order()", DiagnosticId = "MEZ_NET7")]
#endif
    public static IEnumerable<T> Sort<T>(this IEnumerable<T> list, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(list);

        return list.Order(comparer);
    }

    public static int IndexOf<T>(this IEnumerable<T> list, T value)
    {
        ArgumentNullException.ThrowIfNull(list);

        return list.IndexOf(value, comparer: null);
    }

    public static int IndexOf<T>(this IEnumerable<T> list, T value, IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(list);

        comparer ??= EqualityComparer<T>.Default;
        var index = 0;
        foreach (var item in list)
        {
            if (comparer.Equals(item, value))
                return index;

            index++;
        }

        return -1;
    }

    public static long LongIndexOf<T>(this IEnumerable<T> list, T value) where T : IEquatable<T>
    {
        return list.LongIndexOf(value, EqualityComparer<T>.Default);
    }

    public static long LongIndexOf<T>(this IEnumerable<T> list, T value, IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(list);

        comparer ??= EqualityComparer<T>.Default;
        var index = 0L;
        foreach (var item in list)
        {
            if (comparer.Equals(item, value))
                return index;

            checked
            {
                index++;
            }
        }

        return -1L;
    }

    public static bool ContainsIgnoreCase(this IEnumerable<string> str, string value)
    {
        ArgumentNullException.ThrowIfNull(str);

        return str.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    public static void EnumerateAll<TSource>(this IEnumerable<TSource> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
        }
    }

    public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T>? items)
    {
        if (items is null)
            return Enumerable.Empty<T>();

        return items;
    }

    public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> action)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        foreach (var item in source)
        {
            action(item);
        }
    }

    public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource, int> action)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        var index = 0;
        foreach (var item in source)
        {
            action(item, index);
            checked
            {
                index++;
            }
        }
    }

    public static TimeSpan Sum(this IEnumerable<TimeSpan> enumerable)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        var result = TimeSpan.Zero;
        foreach (var item in enumerable)
        {
            result += item;
        }

        return result;
    }

    public static TimeSpan Average(this IEnumerable<TimeSpan> enumerable)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        var result = 0L;
        var count = 0;
        foreach (var item in enumerable)
        {
            result += item.Ticks;

            checked
            {
                count++;
            }
        }

        return TimeSpan.FromTicks(result / count);
    }

    /// <summary>
    /// Ensure the enumerable instance is enumerated only once.
    /// </summary>
    public static IEnumerable<T> AsEnumerableOnce<T>(this IEnumerable<T> enumerable)
    {
        return new EnumerableOnce<T>(enumerable);
    }

    public static IEnumerable<T> ToOnlyEnumerable<T>(this IEnumerable<T> enumerable)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        return ToOnlyEnumerableImpl(enumerable);

        static IEnumerable<T> ToOnlyEnumerableImpl(IEnumerable<T> enumerable)
        {
            foreach (var item in enumerable)
                yield return item;
        }
    }

    public static ReadOnlyCollection<T> ToReadOnlyCollection<T>(this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ReadOnlyCollection<T>(source.ToList());
    }

    public static ICollection<T> ToCollection<T>(this IEnumerable<T> sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        return sequence is ICollection<T> collection ? collection : sequence.ToList();
    }

    [SuppressMessage("Design", "MA0016:Prefer return collection abstraction instead of implementation", Justification = "Similar to Enumerable.ToList()")]
    public static async Task<List<T>> ToListAsync<T>(this Task<IEnumerable<T>> task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var result = await task.ConfigureAwait(false);
        return result.ToList();
    }

    public static async Task<T[]> ToArrayAsync<T>(this Task<IEnumerable<T>> task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var result = await task.ConfigureAwait(false);
        return result.ToArray();
    }

    private sealed class EnumerableOnce<TSource> : IEnumerable<TSource>
    {
        private readonly IEnumerable<TSource> _source;
        private bool _enumerated;

        public EnumerableOnce(IEnumerable<TSource> source) => _source = source;

        public IEnumerator<TSource> GetEnumerator()
        {
            if (_enumerated)
                throw new InvalidOperationException("The source is already enumerated");

            _enumerated = true;
            return _source.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
