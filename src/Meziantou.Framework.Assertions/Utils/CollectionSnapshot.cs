using System.Collections;
using System.Diagnostics;

namespace Meziantou.Framework.Assertions;

internal sealed class CollectionSnapshot<T> : IEnumerable<T>, IDisposable
{
    private readonly List<T> _cache = [];
    private readonly IEnumerable<T> _source;
    private IEnumerator<T>? _enumerator;
    private bool _isComplete;

    public CollectionSnapshot(IEnumerable<T> source)
    {
        _source = source;
    }

    public bool IsComplete => _isComplete;

    public int ObservedCount => _cache.Count;

    public IReadOnlyList<T> Items => _cache;

    public IEnumerator<T> GetEnumerator()
    {
        var index = 0;
        while (TryGetItem(index, out var item))
        {
            yield return item;
            index++;
        }
    }

    private bool TryGetItem(int index, out T item)
    {
        if (index < _cache.Count)
        {
            item = _cache[index];
            return true;
        }

        if (_isComplete)
        {
            item = default!;
            return false;
        }

        _enumerator ??= _source.GetEnumerator();

        Debug.Assert(_enumerator is not null);
        if (_enumerator.MoveNext())
        {
            item = _enumerator.Current;
            _cache.Add(item);
            return true;
        }

        _isComplete = true;
        _enumerator.Dispose();
        _enumerator = null;
        item = default!;

        return false;
    }

    public void Dispose()
    {
        _enumerator?.Dispose();
        _enumerator = null;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
