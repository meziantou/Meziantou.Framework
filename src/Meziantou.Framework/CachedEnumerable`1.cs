using System.Collections;
using System.Diagnostics;

namespace Meziantou.Framework;

internal sealed class CachedEnumerable<T> : ICachedEnumerable<T>
{
    private readonly List<T> _cache = [];
    private readonly IEnumerable<T> _enumerable;
    private IEnumerator<T>? _enumerator;
    private bool _enumerated;

    public CachedEnumerable(IEnumerable<T> enumerable)
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

        if (_enumerator is null && !_enumerated)
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
        Debug.Assert(_enumerator is not null);
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
