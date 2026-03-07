using System.Collections;
using Meziantou.Framework.Collections;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public sealed class InMemoryOpenTelemetryItemCollection : IReadOnlyCollection<OpenTelemetryItem>
{
    private readonly ICollection<OpenTelemetryItem> _items;
    private readonly bool _threadSafe;

    public InMemoryOpenTelemetryItemCollection(int maxItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemCount);
        if (maxItemCount == int.MaxValue)
        {
            _items = new AppendOnlyCollection<OpenTelemetryItem>();
            _threadSafe = true;
        }
        else
        {
            _items = new CircularBuffer<OpenTelemetryItem>(maxItemCount) { AllowOverwrite = true };
        }
    }

    public int Count => _items.Count;

    internal void Add(OpenTelemetryItem item)
    {
        if (_threadSafe)
        {
            _items.Add(item);
        }
        else
        {
            lock (_items)
            {
                _items.Add(item);
            }
        }
    }

    public IEnumerator<OpenTelemetryItem> GetEnumerator()
    {
        if (_threadSafe)
            return _items.GetEnumerator();

        lock (_items)
        {
            var clone = _items.ToList();
            return clone.GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
