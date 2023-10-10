using System.Collections;

namespace Meziantou.Framework.HumanReadable;
internal abstract class ConfigurationList<TItem> : IList<TItem>
{
    protected List<TItem> List { get; }

    protected ConfigurationList(IList<TItem>? source = null)
    {
        List = source is null ? [] : new List<TItem>(source);
    }

    protected abstract bool IsImmutable { get; }
    protected abstract void VerifyMutable();
    protected virtual void OnAddingElement(TItem item) { }

    public TItem this[int index]
    {
        get
        {
            return List[index];
        }
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            VerifyMutable();
            OnAddingElement(value);
            List[index] = value;
        }
    }

    public int Count => List.Count;

    public bool IsReadOnly => IsImmutable;

    public void Add(TItem item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        VerifyMutable();
        OnAddingElement(item);
        List.Add(item);
    }

    public void Clear()
    {
        VerifyMutable();
        List.Clear();
    }

    public bool Contains(TItem item)
    {
        return List.Contains(item);
    }

    public void CopyTo(TItem[] array, int arrayIndex)
    {
        List.CopyTo(array, arrayIndex);
    }

    public List<TItem>.Enumerator GetEnumerator()
    {
        return List.GetEnumerator();
    }

    public int IndexOf(TItem item)
    {
        return List.IndexOf(item);
    }

    public void Insert(int index, TItem item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        VerifyMutable();
        OnAddingElement(item);
        List.Insert(index, item);
    }

    public bool Remove(TItem item)
    {
        VerifyMutable();
        return List.Remove(item);
    }

    public void RemoveAt(int index)
    {
        VerifyMutable();
        List.RemoveAt(index);
    }

    IEnumerator<TItem> IEnumerable<TItem>.GetEnumerator() => List.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => List.GetEnumerator();
}