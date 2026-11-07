using System.Collections;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://drafts.csswg.org/css-view-transitions-2/#viewtransitiontypeset: the types of a view transition, matched by the
/// <c>:active-view-transition-type()</c> pseudo-class. It is a <c>setlike&lt;DOMString&gt;</c>: the items are enumerated in
/// insertion order, and, as for a JavaScript <c>Set</c>, an enumeration sees the changes made while it is in progress.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-view-transitions-2/#viewtransitiontypeset"/>
[IdlInterface("ViewTransitionTypeSet", "https://drafts.csswg.org/css-view-transitions-2/#viewtransitiontypeset")]
public sealed class ViewTransitionTypeSet : IReadOnlyCollection<string>
{
    private readonly ViewTransition _transition;

    // The entries in insertion order; a deleted entry is null until the list is compacted (https://tc39.es/ecma262/#sec-set.prototype.delete)
    private List<string?> _entries = [];
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

    internal ViewTransitionTypeSet(ViewTransition transition)
    {
        _transition = transition;
    }

    /// <summary>Gets the number of types.</summary>
    [NotInIdl("The size member of setlike<DOMString>")]
    public int Count => _indexes.Count;

    /// <summary>Returns whether the set contains the type.</summary>
    [NotInIdl("The has() member of setlike<DOMString>")]
    public bool Has(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _indexes.ContainsKey(value);
    }

    /// <summary>Adds a type.</summary>
    [NotInIdl("The add() member of setlike<DOMString>")]
    public void Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_indexes.TryAdd(value, _entries.Count))
        {
            _entries.Add(value);
            OnChanged();
        }
    }

    /// <summary>Removes a type. Returns whether the set contained the type.</summary>
    [NotInIdl("The delete() member of setlike<DOMString>")]
    public bool Delete(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_indexes.Remove(value, out var index))
            return false;

        _entries[index] = null;
        CompactIfNeeded();
        OnChanged();
        return true;
    }

    /// <summary>Removes all the types.</summary>
    [NotInIdl("The clear() member of setlike<DOMString>")]
    public void Clear()
    {
        if (_indexes.Count == 0)
            return;

        _indexes.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            _entries[i] = null;
        }

        CompactIfNeeded();
        OnChanged();
    }

    /// <summary>
    /// Replaces the list when most entries are deleted, so that it does not grow forever. The enumerations in progress keep
    /// the previous list, where the items added later are not visible.
    /// </summary>
    private void CompactIfNeeded()
    {
        if (_entries.Count < 32 || _indexes.Count * 2 > _entries.Count)
            return;

        var entries = new List<string?>(_indexes.Count);
        foreach (var entry in _entries)
        {
            if (entry is not null)
            {
                _indexes[entry] = entries.Count;
                entries.Add(entry);
            }
        }

        _entries = entries;
    }

    /// <summary>Whether the transition has the type (used by the <c>:active-view-transition-type()</c> pseudo-class).</summary>
    internal bool Contains(string value) => _indexes.ContainsKey(value);

    // The styles that depend on the types of the active view transition are computed again
    private void OnChanged()
    {
        if (ReferenceEquals(_transition.Document.ActiveViewTransition, _transition))
        {
            _transition.Document.IncrementElementStateVersion();
        }
    }

    /// <summary>Enumerates the types, including the types added during the enumeration.</summary>
    [NotInIdl("The iterator of setlike<DOMString>")]
    public IEnumerator<string> GetEnumerator()
    {
        var entries = _entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is { } entry)
                yield return entry;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
