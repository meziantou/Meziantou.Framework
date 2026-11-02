using Meziantou.Framework.Collections;

namespace Meziantou.Framework;

internal sealed class CachedAsyncEnumerableThreadSafe<T> : ICachedAsyncEnumerable<T>
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A consumer may still be waiting for it when the enumerable is disposed. SemaphoreSlim only holds unmanaged resources when its AvailableWaitHandle is used.")]
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly AppendOnlyCollection<T> _cache = [];
    private readonly CachedAsyncEnumerableSource<T> _source;
    private int _disposed;

    public CachedAsyncEnumerableThreadSafe(IAsyncEnumerable<T> enumerable)
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // if the item is in the cache, use it
        if (index < _cache.Count)
            return new(_cache[index]);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            // Another thread may have got the item while we were acquiring the lock
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
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Cancel the source first, so a consumer reading it releases the semaphore sooner
        _source.Cancel();

        // Take the semaphore so the source enumerator is not disposed while another consumer is reading it
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await _source.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
