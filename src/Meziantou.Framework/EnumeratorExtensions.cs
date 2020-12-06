using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework
{
    public static class EnumeratorExtensions
    {
        [SuppressMessage("Design", "MA0016:Prefer return collection abstraction instead of implementation", Justification = "Like Enumerable.ToList<T>()")]
        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Like Enumerable.ToList<T>()")]
        public static List<T> ToList<T>(this IEnumerator<T> enumerator)
        {
            var list = new List<T>();
            while (enumerator.MoveNext())
            {
                list.Add(enumerator.Current);
            }

            return list;
        }

        public static IEnumerable<T> AsEnumerable<T>(this IEnumerator<T> enumerator)
        {
            return new EnumeratorWrapper<T>(enumerator);
        }

        private sealed class EnumeratorWrapper<T> : IEnumerable<T>
        {
            private readonly IEnumerator<T> _enumerator;

            public EnumeratorWrapper(IEnumerator<T> enumerator) => _enumerator = enumerator;

            public IEnumerator<T> GetEnumerator() => _enumerator;
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
