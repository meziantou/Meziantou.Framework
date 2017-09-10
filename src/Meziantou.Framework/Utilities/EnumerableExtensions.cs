using System;
using System.Collections.Generic;
using System.Linq;

namespace Meziantou.Framework.Utilities
{
    public static class EnumerableExtensions
    {
        public static void AddRange<T>(this ICollection<T> collection, params T[] items)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

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
            if (collection == null) throw new ArgumentNullException(nameof(collection));

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
            if (list == null) throw new ArgumentNullException(nameof(list));

            var index = list.IndexOf(oldItem);
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(oldItem));

            list[index] = newItem;
        }

        public static void AddOrReplace<T>(this IList<T> list, T oldItem, T newItem)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));

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
            if (source == null)
                return null;

            var hash = new HashSet<TKey>();
            return source.Where(p => hash.Add(keySelector(p)));
        }

        public static int IndexOf<T>(this IEnumerable<T> list, T value)
        {
            return list.IndexOf(value, EqualityComparer<T>.Default);
        }

        public static int IndexOf<T>(this IEnumerable<T> list, T value, IEqualityComparer<T> comparer)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (comparer == null) throw new ArgumentNullException(nameof(comparer));

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
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (comparer == null) throw new ArgumentNullException(nameof(comparer));

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

            return -1;
        }

        public static bool ContainsIgnoreCase(this IEnumerable<string> str, string value)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));

            foreach (var s in str)
            {
                if (s.EqualsIgnoreCase(value))
                    return true;
            }

            return false;
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
    }
}
