using System.Collections;
using System.Diagnostics;
using Meziantou.Framework.Collections;

namespace Meziantou.Framework;

internal sealed class CachedEnumerableThreadSafe<T> : ICachedEnumerable<T>
{
    private readonly AppendOnlyCollection<T> _cache = [];
    private readonly IEnumerable<T> _enumerable;
    private IEnumerator<T>? _enumerator;
    private bool _enumerated;

    public CachedEnumerableThreadSafe(IEnumerable<T> enumerable)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        _enumerable = enumerable;
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

            // If we have already enumerate the whole stream, there is nothing else to do
            if (_enumerated)
            {
                result = default!;
                return false;
            }

            _enumerator ??= _enumerable.GetEnumerator();

            // Get the next item and store it to the cache
            if (_enumerator.MoveNext())
            {
                result = _enumerator.Current;
                _cache.Add(result);
                return true;
            }
            else
            {
                // There are no more items, we can dispose the underlying enumerator
                _enumerated = true;
                _enumerator.Dispose();
                _enumerator = null;
                result = default!;
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (_enumerator is not null)
        {
            _enumerator.Dispose();
            _enumerator = null;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
