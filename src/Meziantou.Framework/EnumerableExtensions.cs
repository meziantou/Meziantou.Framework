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
        if (collection is null)
            throw new ArgumentNullException(nameof(collection));

        if (items is null)
            throw new ArgumentNullException(nameof(items));

        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T>? items)
    {
        if (collection is null)
            throw new ArgumentNullException(nameof(collection));

        if (items != null)
        {
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }
    }

    public static void Replace<T>(this IList<T> list, T oldItem, T newItem)
    {
        if (list is null)
            throw new ArgumentNullException(nameof(list));

        var index = list.IndexOf(oldItem);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(oldItem));

        list[index] = newItem;
    }

    public static void AddOrReplace<T>(this IList<T> list, T? oldItem, T newItem)
    {
        if (list is null)
            throw new ArgumentNullException(nameof(list));

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
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return source.Where(item => item != null)!;
    }

    public static IEnumerable<string> WhereNotNullOrEmpty(this IEnumerable<string?> source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return source.Where(item => !string.IsNullOrEmpty(item))!;
    }

    public static IEnumerable<string> WhereNotNullOrWhiteSpace(this IEnumerable<string?> source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return source.Where(item => !string.IsNullOrWhiteSpace(item))!;
    }

#if NET6_0_OR_GREATER
    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
#elif NETSTANDARD2_0
    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
#else
#error Platform not supported
#endif
    {
        return DistinctBy(source, keySelector, EqualityComparer<TKey>.Default);
    }


#if NET6_0_OR_GREATER
    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
#elif NETSTANDARD2_0
    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
#else
#error Platform not supported
#endif
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        if (keySelector is null)
            throw new ArgumentNullException(nameof(keySelector));

        var hash = new HashSet<TKey>(comparer);
        return source.Where(p => hash.Add(keySelector(p)));
    }

    public static bool IsDistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        if (keySelector is null)
            throw new ArgumentNullException(nameof(keySelector));

        return IsDistinctBy(source, keySelector, EqualityComparer<TKey>.Default);
    }

    public static bool IsDistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        if (keySelector is null)
            throw new ArgumentNullException(nameof(keySelector));

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
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return IsDistinct(source, EqualityComparer<TSource>.Default);
    }

    public static bool IsDistinct<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

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
        if (list is null)
            throw new ArgumentNullException(nameof(list));

        return list.OrderBy(item => item, comparer);
    }

    public static int IndexOf<T>(this IEnumerable<T> list, T value)
    {
        if (list is null)
            throw new ArgumentNullException(nameof(list));

        return list.IndexOf(value, comparer: null);
    }

    public static int IndexOf<T>(this IEnumerable<T> list, T value, IEqualityComparer<T>? comparer)
    {
        if (list is null)
            throw new ArgumentNullException(nameof(list));

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
        if (list is null)
            throw new ArgumentNullException(nameof(list));

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
        if (str is null)
            throw new ArgumentNullException(nameof(str));

        return str.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    public static void EnumerateAll<TSource>(this IEnumerable<TSource> source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        using var enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
        }
    }

    public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T>? items)
    {
        if (items == null)
            return Enumerable.Empty<T>();

        return items;
    }

    public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource> action)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        foreach (var item in source)
        {
            action(item);
        }
    }

    public static void ForEach<TSource>(this IEnumerable<TSource> source, Action<TSource, int> action)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (action is null)
            throw new ArgumentNullException(nameof(action));

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


#if NET6_0_OR_GREATER
    public static T MaxBy<T, TValue>(IEnumerable<T> enumerable, Func<T, TValue> selector)
#elif NETSTANDARD2_0
    public static T MaxBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue> selector)
#else
#error Platform not supported
#endif
        where TValue : IComparable
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        if (selector is null)
            throw new ArgumentNullException(nameof(selector));

        var enumerator = enumerable.GetEnumerator();
        try
        {
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection is empty", nameof(enumerable));

            var maxElem = enumerator.Current;
            var maxVal = selector(maxElem);

            while (enumerator.MoveNext())
            {
                var currVal = selector(enumerator.Current);

                if (currVal.CompareTo(maxVal) > 0)
                {
                    maxVal = currVal;
                    maxElem = enumerator.Current;
                }
            }

            return maxElem;
        }
        finally
        {
            enumerator.Dispose();
        }
    }

#if NET6_0_OR_GREATER
    public static T MaxBy<T, TValue>(IEnumerable<T> enumerable, Func<T, TValue?> selector, IComparer<TValue>? comparer)
#elif NETSTANDARD2_0
    public static T MaxBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue?> selector, IComparer<TValue>? comparer)
#else
#error Platform not supported
#endif
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        if (selector is null)
            throw new ArgumentNullException(nameof(selector));

        comparer ??= Comparer<TValue>.Default;

        var enumerator = enumerable.GetEnumerator();
        try
        {
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection is empty", nameof(enumerable));

            var maxElem = enumerator.Current;
            var maxVal = selector(maxElem);

            while (enumerator.MoveNext())
            {
                var currVal = selector(enumerator.Current);

                if (comparer.Compare(currVal, maxVal) > 0)
                {
                    maxVal = currVal;
                    maxElem = enumerator.Current;
                }
            }

            return maxElem;
        }
        finally
        {
            enumerator.Dispose();
        }
    }

#if NET6_0_OR_GREATER
    public static T Max<T>(IEnumerable<T> enumerable, IComparer<T>? comparer)
#elif NETSTANDARD2_0
    public static T Max<T>(this IEnumerable<T> enumerable, IComparer<T>? comparer)
#else
#error Platform not supported
#endif
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        comparer ??= Comparer<T>.Default;

        var enumerator = enumerable.GetEnumerator();
        try
        {
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection is empty", nameof(enumerable));

            var maxVal = enumerator.Current;

            while (enumerator.MoveNext())
            {
                var currVal = enumerator.Current;
                if (comparer.Compare(currVal, maxVal) > 0)
                {
                    maxVal = currVal;
                }
            }

            return maxVal;
        }
        finally
        {
            enumerator.Dispose();
        }
    }


#if NET6_0_OR_GREATER
    public static T MinBy<T, TValue>(IEnumerable<T> enumerable, Func<T, TValue> selector) where TValue : IComparable
#elif NETSTANDARD2_0
    public static T MinBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue> selector) where TValue : IComparable
#else
#error Platform not supported
#endif
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        if (selector is null)
            throw new ArgumentNullException(nameof(selector));

        var enumerator = enumerable.GetEnumerator();
        try
        {
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection is empty", nameof(enumerable));

            var minElem = enumerator.Current;
            var minVal = selector(minElem);

            while (enumerator.MoveNext())
            {
                var currVal = selector(enumerator.Current);

                if (currVal.CompareTo(minVal) < 0)
                {
                    minVal = currVal;
                    minElem = enumerator.Current;
                }
            }

            return minElem;
        }
        finally
        {
            enumerator.Dispose();
        }
    }


#if NET6_0_OR_GREATER
    public static T MinBy<T, TValue>(IEnumerable<T> enumerable, Func<T, TValue?> selector, IComparer<TValue>? comparer)
#elif NETSTANDARD2_0
    public static T MinBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue?> selector, IComparer<TValue>? comparer)
#else
#error Platform not supported
#endif
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        if (selector is null)
            throw new ArgumentNullException(nameof(selector));

        comparer ??= Comparer<TValue>.Default;

        var enumerator = enumerable.GetEnumerator();
        try
        {
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection is empty", nameof(enumerable));

            var minElem = enumerator.Current;
            var minVal = selector(minElem);

            while (enumerator.MoveNext())
            {
                var currVal = selector(enumerator.Current);

                if (comparer.Compare(currVal, minVal) < 0)
                {
                    minVal = currVal;
                    minElem = enumerator.Current;
                }
            }

            return minElem;
        }
        finally
        {
            enumerator.Dispose();
        }
    }
#if NET6_0_OR_GREATER
    public static T Min<T>(IEnumerable<T> enumerable, IComparer<T>? comparer)
#elif NETSTANDARD2_0
    public static T Min<T>(this IEnumerable<T> enumerable, IComparer<T>? comparer)
#else
#error Platform not supported
#endif
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        comparer ??= Comparer<T>.Default;

        var enumerator = enumerable.GetEnumerator();
        try
        {
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection is empty", nameof(enumerable));

            var minVal = enumerator.Current;

            while (enumerator.MoveNext())
            {
                var currVal = enumerator.Current;
                if (comparer.Compare(currVal, minVal) < 0)
                {
                    minVal = currVal;
                }
            }

            return minVal;
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    public static TimeSpan Sum(this IEnumerable<TimeSpan> enumerable)
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        var result = TimeSpan.Zero;
        foreach (var item in enumerable)
        {
            result += item;
        }

        return result;
    }

    public static TimeSpan Average(this IEnumerable<TimeSpan> enumerable)
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

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

    public static IEnumerable<T> ToOnlyEnumerable<T>(this IEnumerable<T> enumerable)
    {
        if (enumerable is null)
            throw new ArgumentNullException(nameof(enumerable));

        return ToOnlyEnumerableImpl(enumerable);

        static IEnumerable<T> ToOnlyEnumerableImpl(IEnumerable<T> enumerable)
        {
            foreach (var item in enumerable)
                yield return item;
        }
    }

    public static ReadOnlyCollection<T> ToReadOnlyCollection<T>(this IEnumerable<T> source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return new ReadOnlyCollection<T>(source.ToList());
    }

    public static ICollection<T> ToCollection<T>(this IEnumerable<T> sequence)
    {
        if (sequence is null)
            throw new ArgumentNullException(nameof(sequence));

        return sequence is ICollection<T> collection ? collection : sequence.ToList();
    }

    [SuppressMessage("Design", "MA0016:Prefer return collection abstraction instead of implementation", Justification = "Similar to Enumerable.ToList()")]
    public static async Task<List<T>> ToListAsync<T>(this Task<IEnumerable<T>> task)
    {
        if (task is null)
            throw new ArgumentNullException(nameof(task));

        var result = await task.ConfigureAwait(false);
        return result.ToList();
    }

    public static async Task<T[]> ToArrayAsync<T>(this Task<IEnumerable<T>> task)
    {
        if (task is null)
            throw new ArgumentNullException(nameof(task));

        var result = await task.ConfigureAwait(false);
        return result.ToArray();
    }
}
