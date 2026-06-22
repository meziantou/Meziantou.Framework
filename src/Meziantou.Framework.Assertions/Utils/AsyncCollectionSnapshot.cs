using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Assertions;

[SuppressMessage("Maintainability", "MA0182:Unused internal types", Justification = "Reserved for async collection assertions.")]
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

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var index = 0;
        while (await TryGetItem(index, cancellationToken).ConfigureAwait(false) is (true, var item))
        {
            yield return item;
            index++;
        }
    }

    private async ValueTask<(bool Success, T Item)> TryGetItem(int index, CancellationToken cancellationToken)
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
}
