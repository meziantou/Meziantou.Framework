using System.Collections;
using System.Diagnostics;

namespace Meziantou.Framework
{
    internal sealed class CachedEnumerableThreadSafe<T> : ICachedEnumerable<T>
    {
        private readonly List<T> _cache = new();
        private readonly IEnumerable<T> _enumerable;
        private IEnumerator<T>? _enumerator;
        private bool _enumerated;

        public CachedEnumerableThreadSafe(IEnumerable<T> enumerable)
        {
            _enumerable = enumerable ?? throw new ArgumentNullException(nameof(enumerable));
        }

        public IEnumerator<T> GetEnumerator()
        {
            var index = 0;
            while (true)
            {
                if (TryGetItem(index, out var result))
                {
                    yield return result;
                    index++;
                }
                else
                {
                    // There are no more items
                    yield break;
                }
            }
        }

        private bool TryGetItem(int index, out T result)
        {
            // if the item is in the cache, use it
            if (index < _cache.Count)
            {
                result = _cache[index];
                return true;
            }

            lock (_cache)
            {
                // Another thread may have got the item while we were acquiring the lock
                if (index < _cache.Count)
                {
                    result = _cache[index];
                    return true;
                }

                if (_enumerator == null && !_enumerated)
                {
                    _enumerator = _enumerable.GetEnumerator();
                }

                // If we have already enumerate the whole stream, there is nothing else to do
                if (_enumerated)
                {
                    result = default!;
                    return false;
                }

                // Get the next item and store it to the cache
                Debug.Assert(_enumerator != null);
                if (_enumerator.MoveNext())
                {
                    result = _enumerator.Current;
                    _cache.Add(result);
                    return true;
                }
                else
                {
                    // There are no more items, we can dispose the underlying enumerator
                    _enumerator.Dispose();
                    _enumerator = null;
                    _enumerated = true;
                    result = default!;
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (_enumerator != null)
            {
                _enumerator.Dispose();
                _enumerator = null;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
