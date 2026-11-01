namespace Meziantou.Framework;

internal sealed class CachedAsyncEnumerable<T> : ICachedAsyncEnumerable<T>
{
    private readonly List<T> _cache = [];
    private readonly CachedAsyncEnumerableSource<T> _source;
    private bool _disposed;

    public CachedAsyncEnumerable(IAsyncEnumerable<T> enumerable)
    {
        _source = new CachedAsyncEnumerableSource<T>(enumerable);
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var index = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        // if the item is in the cache, use it
        if (index < _cache.Count)
            return new(_cache[index]);

        // Get the next item and store it to the cache
        var result = await _source.MoveNextAsync(cancellationToken).ConfigureAwait(false);
        if (result.HasValue)
        {
            _cache.Add(result.Value);
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _source.DisposeAsync().ConfigureAwait(false);
    }
}
