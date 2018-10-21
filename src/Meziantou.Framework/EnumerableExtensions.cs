using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    public static class EnumerableExtensions
    {
        public static void AddRange<T>(this ICollection<T> collection, params T[] items)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof(collection));

            if (items != null)
            {
                foreach (var item in items)
                {
                    collection.Add(item);
                }
            }
        }

        public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
        {
            if (collection == null)
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
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            var index = list.IndexOf(oldItem);
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(oldItem));

            list[index] = newItem;
        }

        public static void AddOrReplace<T>(this IList<T> list, T oldItem, T newItem)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            var index = list.IndexOf(oldItem);
            if (index < 0)
            {
                list.Add(newItem);
            }
            else
            {
                list[index] = newItem;
            }
        }

        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T> source) where T : class
        {
            if (source == null)
                return null;

            return source.Where(item => item != null);
        }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            return DistinctBy(source, keySelector, EqualityComparer<TKey>.Default);
        }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            if (source == null)
                return null;

            var hash = new HashSet<TKey>(comparer);
            return source.Where(p => hash.Add(keySelector(p)));
        }

        public static IEnumerable<T> Sort<T>(this IEnumerable<T> list)
        {
            return Sort(list, Comparer<T>.Default);
        }

        public static IEnumerable<T> Sort<T>(this IEnumerable<T> list, IComparer<T> comparer)
        {
            return list.OrderBy(item => item, comparer);
        }

        public static int IndexOf<T>(this IEnumerable<T> list, T value)
        {
            return list.IndexOf(value, EqualityComparer<T>.Default);
        }

        public static int IndexOf<T>(this IEnumerable<T> list, T value, IEqualityComparer<T> comparer)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (comparer == null)
                throw new ArgumentNullException(nameof(comparer));

            var index = 0;
            using (var enumerator = list.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    if (comparer.Equals(enumerator.Current, value))
                        return index;

                    index++;
                }
            }

            return -1;
        }

        public static long LongIndexOf<T>(this IEnumerable<T> list, T value) where T : IEquatable<T>
        {
            return list.LongIndexOf(value, EqualityComparer<T>.Default);
        }

        public static long LongIndexOf<T>(this IEnumerable<T> list, T value, IEqualityComparer<T> comparer)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (comparer == null)
                throw new ArgumentNullException(nameof(comparer));

            var index = 0L;
            using (var enumerator = list.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    if (comparer.Equals(enumerator.Current, value))
                        return index;

                    index++;
                }
            }

            return -1L;
        }

        public static bool ContainsIgnoreCase(this IEnumerable<string> str, string value)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));

            return str.Contains(value, StringComparer.OrdinalIgnoreCase);
        }

        public static void EnumerateAll<TSource>(this IEnumerable<TSource> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                }
            }
        }

        public static Task ForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action)
        {
            return ForEachAsync(source, Environment.ProcessorCount, action);
        }

        public static Task ForEachAsync<TSource>(this IEnumerable<TSource> source, int degreeOfParallelism, Func<TSource, Task> action)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var tasks = from partition in Partitioner.Create(source).GetPartitions(degreeOfParallelism)
                        select Task.Run(async () =>
                        {
                            using (partition)
                            {
                                while (partition.MoveNext())
                                {
                                    await action(partition.Current);
                                }
                            }
                        });

            return Task.WhenAll(tasks);
        }

        public static T MaxBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue> selector) where TValue : IComparable
        {
            if (enumerable == null)
                throw new ArgumentNullException(nameof(enumerable));

            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            var enumerator = enumerable.GetEnumerator();
            try
            {
                if (!enumerator.MoveNext())
                    throw new ArgumentException("Collection is empty");

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

        public static T MaxBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue> selector, IComparer<TValue> comparer)
        {
            if (enumerable == null)
                throw new ArgumentNullException(nameof(enumerable));

            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            if (comparer == null)
                throw new ArgumentNullException(nameof(comparer));

            var enumerator = enumerable.GetEnumerator();
            try
            {
                if (!enumerator.MoveNext())
                    throw new ArgumentException("Collection is empty");

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

        public static T Max<T>(this IEnumerable<T> enumerable, IComparer<T> comparer)
        {
            if (enumerable == null)
                throw new ArgumentNullException(nameof(enumerable));

            if (comparer == null)
                throw new ArgumentNullException(nameof(comparer));

            var enumerator = enumerable.GetEnumerator();
            try
            {
                if (!enumerator.MoveNext())
                    throw new ArgumentException("Collection is empty");

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

        public static T MinBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue> selector) where TValue : IComparable
        {
            if (enumerable == null)
                throw new ArgumentNullException(nameof(enumerable));

            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            var enumerator = enumerable.GetEnumerator();
            try
            {
                if (!enumerator.MoveNext())
                    throw new ArgumentException("Collection is empty");

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

        public static T MinBy<T, TValue>(this IEnumerable<T> enumerable, Func<T, TValue> selector, IComparer<TValue> comparer)
        {
            if (enumerable == null)
                throw new ArgumentNullException(nameof(enumerable));

            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            if (comparer == null)
                throw new ArgumentNullException(nameof(comparer));

            var enumerator = enumerable.GetEnumerator();
            try
            {
                if (!enumerator.MoveNext())
                    throw new ArgumentException("Collection is empty");

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

        public static T Min<T>(this IEnumerable<T> enumerable, IComparer<T> comparer)
        {
            if (enumerable == null)
                throw new ArgumentNullException(nameof(enumerable));

            if (comparer == null)
                throw new ArgumentNullException(nameof(comparer));

            var enumerator = enumerable.GetEnumerator();
            try
            {
                if (!enumerator.MoveNext())
                    throw new ArgumentException("Collection is empty");

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
            var result = TimeSpan.Zero;
            foreach (var item in enumerable)
            {
                result += item;
            }

            return result;
        }

        public static TimeSpan Average(this IEnumerable<TimeSpan> enumerable)
        {
            var result = 0L;
            var count = 0;
            foreach (var item in enumerable)
            {
                result += item.Ticks;
                count++;
            }

            return TimeSpan.FromTicks(result / count);
        }
    }
}
