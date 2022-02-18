using System.Collections;

namespace Meziantou.Framework.CodeDom;

public class MethodArgumentCollection : CodeObject, IList<MethodArgumentDeclaration>, IReadOnlyList<MethodArgumentDeclaration>
{
    private readonly List<MethodArgumentDeclaration> _list = new();

    public MethodArgumentCollection()
    {
    }

    public MethodArgumentCollection(CodeObject parent!!)
    {
        Parent = parent;
    }

    public IEnumerator<MethodArgumentDeclaration> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)_list).GetEnumerator();
    }

    public void AddRange(IEnumerable<MethodArgumentDeclaration> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    void ICollection<MethodArgumentDeclaration>.Add(MethodArgumentDeclaration item) => Add(item);

    public MethodArgumentDeclaration Add(MethodArgumentDeclaration item!!)
    {
        _list.Add(item);
        item.Parent = Parent;
        return item;
    }

    public MethodArgumentDeclaration Add(TypeReference type, string name)
    {
        return Add(new MethodArgumentDeclaration(type, name));
    }

    public MethodArgumentDeclaration Add(TypeReference type, string name, Direction direction)
    {
        return Add(new MethodArgumentDeclaration(type, name) { Direction = direction });
    }

    public void Clear()
    {
        foreach (var item in _list)
        {
            item.Parent = null;
        }
        _list.Clear();
    }

    public bool Contains(MethodArgumentDeclaration item)
    {
        return _list.Contains(item);
    }

    public void CopyTo(MethodArgumentDeclaration[] array, int arrayIndex)
    {
        _list.CopyTo(array, arrayIndex);
    }

    public bool Remove(MethodArgumentDeclaration item)
    {
        var remove = _list.Remove(item);
        if (remove)
        {
            item.Parent = null;
        }
        return remove;
    }

    public int Count => _list.Count;

    public bool IsReadOnly => ((IList<MethodArgumentDeclaration>)_list).IsReadOnly;

    public int IndexOf(MethodArgumentDeclaration item)
    {
        return _list.IndexOf(item);
    }

    public void Insert(int index, MethodArgumentDeclaration item)
    {
        _list.Insert(index, item);
        item.Parent = Parent;
    }

    public void RemoveAt(int index)
    {
        var item = this[index];
        if (item == null)
            return;

        _list.RemoveAt(index);
        item.Parent = null;
    }

    [SuppressMessage("Style", "IDE0016:Use 'throw' expression", Justification = "It would change the behavior")]
    public MethodArgumentDeclaration this[int index]
    {
        get => _list[index];
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var item = this[index];
            _list[index] = value;
            if (item != null)
            {
                item.Parent = null;
            }
            value.Parent = Parent;
        }
    }

    public void Sort(IComparer<MethodArgumentDeclaration> comparer)
    {
        _list.Sort(comparer);
    }
}
