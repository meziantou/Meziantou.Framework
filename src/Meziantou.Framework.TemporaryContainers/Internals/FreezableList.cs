using System.Collections.ObjectModel;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>A list that can no longer be changed once the definition that holds it belongs to a container. <see langword="null"/> items are rejected.</summary>
internal sealed class FreezableList<T> : Collection<T>
    where T : notnull
{
    private bool _isReadOnly;

    public FreezableList()
    {
    }

    public FreezableList(IEnumerable<T> items)
    {
        foreach (var item in items)
            Items.Add(item);
    }

    public void MakeReadOnly() => _isReadOnly = true;

    protected override void InsertItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        base.ClearItems();
    }
}
