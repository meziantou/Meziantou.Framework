using System.Collections;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework;

internal sealed class CachedEnumerable<T> : ICachedEnumerable<T>
{
    private readonly List<T> _cache = [];
    private readonly IEnumerable<T> _enumerable;
    private IEnumerator<T>? _enumerator;
    private ExceptionDispatchInfo? _exception;
    private bool _enumerated;
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        // if the item is in the cache, use it
        if (index < _cache.Count)
        {
            result = _cache[index];
            return true;
        }

        // The source failed: replay its exception instead of ending the sequence early
        _exception?.Throw();

        // If we have already enumerate the whole stream, there is nothing else to do
        if (_enumerated)
        {
            result = default!;
            return false;
        }

        _enumerator ??= _enumerable.GetEnumerator();

        // Get the next item and store it to the cache
        bool hasNext;
        try
        {
            hasNext = _enumerator.MoveNext();
        }
        catch (Exception ex)
        {
            // The source enumerator cannot be resumed after an exception
            _exception = ExceptionDispatchInfo.Capture(ex);
            DisposeEnumerator();
            throw;
        }

        if (hasNext)
        {
            result = _enumerator.Current;
            _cache.Add(result);
            return true;
        }

        // There are no more items, we can dispose the underlying enumerator
        _enumerated = true;
        DisposeEnumerator();
        result = default!;
        return false;
    }

    private void DisposeEnumerator()
    {
        var enumerator = _enumerator;
        _enumerator = null;
        enumerator?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        DisposeEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
