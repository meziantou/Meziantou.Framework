using System.Diagnostics;
using Meziantou.Framework.Collections;

namespace Meziantou.Framework;

internal sealed class CachedAsyncEnumerableThreadSafe<T> : ICachedAsyncEnumerable<T>
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly AppendOnlyCollection<T> _cache = [];
    private readonly IAsyncEnumerable<T> _enumerable;
    private IAsyncEnumerator<T>? _enumerator;
    private bool _enumerated;

    public CachedAsyncEnumerableThreadSafe(IAsyncEnumerable<T> enumerable)
    {
        ArgumentNullException.ThrowIfNull(enumerable);

        _enumerable = enumerable;
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var index = 0;
        while (true)
        {
            var item = await TryGetItem(index, cancellationToken).ConfigureAwait(false);
            if (item.HasValue)
            {
                yield return item.Value;
                index++;
            }
            else
            {
                // There are no more items
                yield break;
            }
        }
    }

    private async ValueTask<Optional<T>> TryGetItem(int index, CancellationToken cancellationToken)
    {
        // if the item is in the cache, use it
        if (index < _cache.Count)
        {
            var result = _cache[index];
            return new(result);
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another thread may have got the item while we were acquiring the lock
            if (index < _cache.Count)
            {
                var result = _cache[index];
                return new(result);
            }

            if (_enumerator is null && !_enumerated)
            {
                _enumerator = _enumerable.GetAsyncEnumerator(cancellationToken);
            }

            // If we have already enumerate the whole stream, there is nothing else to do
            if (_enumerated)
                return new();

            // Get the next item and store it to the cache
            Debug.Assert(_enumerator is not null);
            if (await _enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                var result = _enumerator.Current;
                _cache.Add(result);
                return new(result);
            }
            else
            {
                // There are no more items, we can dispose the underlying enumerator
                await _enumerator.DisposeAsync().ConfigureAwait(false);
                _enumerator = null;
                _enumerated = true;
                return new();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_enumerator is not null)
        {
            await _enumerator.DisposeAsync().ConfigureAwait(false);
            _enumerator = null;
        }

        _semaphore.Dispose();
    }
}
