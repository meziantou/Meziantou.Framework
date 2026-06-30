using System.Diagnostics;

namespace Meziantou.Framework.Assertions;

internal sealed class AsyncCollectionSnapshot<T> : IAsyncEnumerable<T>, IAsyncDisposable
{
    private readonly List<T> _cache = [];
    private readonly IAsyncEnumerable<T> _source;
    private IAsyncEnumerator<T>? _enumerator;
    private bool _isComplete;

    public AsyncCollectionSnapshot(IAsyncEnumerable<T> source)
    {
        _source = source;
    }

    public bool IsComplete => _isComplete;

    public int ObservedCount => _cache.Count;

    public IReadOnlyList<T> Items => _cache;

    public Enumerator GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new Enumerator(this, cancellationToken);
    }

    public async ValueTask<(bool Success, T Item)> TryGetItem(int index, CancellationToken cancellationToken = default)
    {
        if (index < _cache.Count)
        {
            return (true, _cache[index]);
        }

        if (_isComplete)
        {
            return (false, default!);
        }

        _enumerator ??= _source.GetAsyncEnumerator(cancellationToken);

        Debug.Assert(_enumerator is not null);
        if (await _enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            var item = _enumerator.Current;
            _cache.Add(item);

            return (true, item);
        }

        _isComplete = true;
        await _enumerator.DisposeAsync().ConfigureAwait(false);
        _enumerator = null;

        return (false, default!);
    }

    public async ValueTask DisposeAsync()
    {
        if (_enumerator is not null)
        {
            await _enumerator.DisposeAsync().ConfigureAwait(false);
            _enumerator = null;
        }
    }

    IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken)
    {
        return GetAsyncEnumerator(cancellationToken);
    }

    public sealed class Enumerator(AsyncCollectionSnapshot<T> snapshot, CancellationToken cancellationToken) : IAsyncEnumerator<T>
    {
        private int _index = -1;

        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            _index++;
            var (success, item) = await snapshot.TryGetItem(_index, cancellationToken).ConfigureAwait(false);
            if (success)
            {
                Current = item;
                return true;
            }

            Current = default!;

            return false;
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
