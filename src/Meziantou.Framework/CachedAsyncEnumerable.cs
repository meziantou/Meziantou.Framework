using System.Diagnostics;

namespace Meziantou.Framework;

internal sealed class CachedAsyncEnumerable<T> : ICachedAsyncEnumerable<T>
{
    private readonly List<T> _cache = [];
    private readonly IAsyncEnumerable<T> _enumerable;
    private IAsyncEnumerator<T>? _enumerator;
    private bool _enumerated;

    public CachedAsyncEnumerable(IAsyncEnumerable<T> enumerable)
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
            return new(_cache[index]);

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
            return new(default!);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_enumerator is not null)
        {
            await _enumerator.DisposeAsync().ConfigureAwait(false);
            _enumerator = null;
        }
    }
}
