namespace Meziantou.Framework.WasmEngine.Components.Runtime;

/// <summary>A table of handles (canonical ABI "Table"): index 0 is never used, the free indices are reused (last freed first).</summary>
internal sealed class ComponentTable<T>
    where T : class
{
    public const int MaxLength = (1 << 28) - 1;

    private readonly List<T?> _array = [null];
    private readonly Stack<int> _free = new();

    public IEnumerable<T> Items
    {
        get
        {
            foreach (var item in _array)
            {
                if (item is not null)
                    yield return item;
            }
        }
    }

    public T Get(long index)
    {
        if (index < 0 || index >= _array.Count || _array[(int)index] is not { } item)
            throw ComponentTrap.Create("unknown handle index " + index.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return item;
    }

    public bool TryGet(long index, out T? item)
    {
        if (index < 0 || index >= _array.Count)
        {
            item = null;
            return false;
        }

        item = _array[(int)index];
        return item is not null;
    }

    public int Add(T item)
    {
        if (_free.TryPop(out var index))
        {
            _array[index] = item;
            return index;
        }

        index = _array.Count;
        if (index > MaxLength)
            throw ComponentTrap.Create("handle table is full");

        _array.Add(item);
        return index;
    }

    public T Remove(long index)
    {
        var item = Get(index);
        _array[(int)index] = null;
        _free.Push((int)index);
        return item;
    }
}

/// <summary>Creates the traps of the component model.</summary>
internal static class ComponentTrap
{
    public static WasmTrapException Create(string message)
    {
        ComponentThread.Current?.Task.Instance.Store.LockDown();
        return new WasmTrapException(message);
    }

    public static void ThrowIf(bool condition, string message)
    {
        if (condition)
            throw Create(message);
    }
}
