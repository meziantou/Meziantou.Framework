using System.Collections;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The items of a level of the document outline (ISO 32000-2, 12.3.3).</summary>
/// <remarks>
/// <para>
/// The outline of a file is read when it is first accessed; a save writes it back: the links between the items are
/// rewritten, and the other properties of an item read from the file only when they were changed.
/// </para>
/// <para>
/// An item is in one collection at most, once: adding an item that is already in a collection, or that contains the
/// collection, throws <see cref="InvalidOperationException"/>. Remove it from its collection first.
/// </para>
/// </remarks>
public sealed class PdfOutlineCollection : IList<PdfOutlineItem>, IReadOnlyList<PdfOutlineItem>
{
    private const int MaxItems = 100_000;
    private const int MaxDepth = 64;

    private readonly List<PdfOutlineItem> _items = [];

    internal PdfOutlineCollection(PdfOutlineItem? owner = null)
    {
        Owner = owner;
    }

    /// <summary>Gets the item whose children the collection holds, or <see langword="null"/> for the top level.</summary>
    internal PdfOutlineItem? Owner { get; }

    /// <summary>Gets the number of items.</summary>
    public int Count => _items.Count;

    bool ICollection<PdfOutlineItem>.IsReadOnly => false;

    /// <summary>Gets or sets an item.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The item.</returns>
    public PdfOutlineItem this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var current = _items[index];
            if (ReferenceEquals(current, value))
                return;

            Attach(value);
            current.Collection = null;
            _items[index] = value;
        }
    }

    /// <summary>Adds an item.</summary>
    /// <param name="title">The text of the item.</param>
    /// <param name="destination">The destination, or <see langword="null"/>.</param>
    /// <returns>The new item.</returns>
    public PdfOutlineItem Add(string title, PdfDestination? destination = null)
    {
        var item = new PdfOutlineItem(title, destination);
        Attach(item);
        _items.Add(item);
        return item;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The item is already in a collection, or it contains this collection.</exception>
    public void Add(PdfOutlineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Attach(item);
        _items.Add(item);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The item is already in a collection, or it contains this collection.</exception>
    public void Insert(int index, PdfOutlineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_items.Count, nameof(index));
        Attach(item);
        _items.Insert(index, item);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        foreach (var item in _items)
        {
            item.Collection = null;
        }

        _items.Clear();
    }

    /// <inheritdoc/>
    public bool Contains(PdfOutlineItem item) => _items.Contains(item);

    /// <inheritdoc/>
    public void CopyTo(PdfOutlineItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public int IndexOf(PdfOutlineItem item) => _items.IndexOf(item);

    /// <inheritdoc/>
    public bool Remove(PdfOutlineItem item)
    {
        if (item is null || !ReferenceEquals(item.Collection, this))
            return false;

        item.Collection = null;
        return _items.Remove(item);
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        _items[index].Collection = null;
        _items.RemoveAt(index);
    }

    /// <inheritdoc/>
    public IEnumerator<PdfOutlineItem> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Makes the collection the collection of an item: the item must not be in a collection, nor be this collection's item or one of its ancestors.</summary>
    private void Attach(PdfOutlineItem item)
    {
        if (item.Collection is not null)
            throw new InvalidOperationException("The item is already in a collection of the outline: remove it first");

        for (var (ancestor, depth) = (Owner, 0); ancestor is not null && depth <= MaxItems; (ancestor, depth) = (ancestor.Collection?.Owner, depth + 1))
        {
            if (ReferenceEquals(ancestor, item))
                throw new InvalidOperationException("The item cannot be added to its own children or descendants");
        }

        item.Collection = this;
    }

    internal static PdfOutlineCollection Load(PdfDocument document)
    {
        var collection = new PdfOutlineCollection();
        if (document.Catalog.Get<PdfDictionary>(PdfNames.Outlines) is { } root)
        {
            var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { root };
            var count = 0;
            collection.LoadChildren(document, root, visited, ref count, depth: 0);
        }

        return collection;
    }

    /// <summary>Writes the outline to the objects of the document.</summary>
    internal void WriteTo(PdfDocument document)
    {
        var catalog = document.Catalog;
        if (_items.Count == 0)
        {
            catalog.Remove(PdfNames.Outlines);
            return;
        }

        PdfReference rootReference;
        PdfDictionary root;
        if (catalog[PdfNames.Outlines] is PdfReference existing && existing.Resolve() is PdfDictionary existingRoot)
        {
            rootReference = existing;
            root = existingRoot;
        }
        else
        {
            root = new PdfDictionary { [PdfNames.Type] = PdfNames.Outlines };
            rootReference = document.Objects.Add(root);
            catalog[PdfNames.Outlines] = rootReference;
        }

        var visible = WriteChildren(document, rootReference, root, depth: 0);
        root[PdfNames.Count] = PdfInteger.Get(visible);
    }

    /// <summary>Writes the items as children of a node and returns the number of visible descendants.</summary>
    private int WriteChildren(PdfDocument document, PdfReference parentReference, PdfDictionary parent, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException("The outline is nested too deeply");

        var visible = 0;
        foreach (var item in _items)
        {
            if (item.Dictionary is null || item.Reference is null || !ReferenceEquals(item.Reference.Owner, document.Objects))
            {
                item.Dictionary = new PdfDictionary();
                item.Reference = document.Objects.Add(item.Dictionary);
                item.Changes = OutlineItemChanges.All;
            }
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var dictionary = item.Dictionary!;
            dictionary[PdfNames.Parent] = parentReference;
            dictionary[PdfNames.Prev] = i > 0 ? _items[i - 1].Reference : null;
            dictionary[PdfNames.Next] = i + 1 < _items.Count ? _items[i + 1].Reference : null;
            if (item.Changes.HasFlag(OutlineItemChanges.Title))
            {
                dictionary[PdfNames.Title] = PdfString.FromText(item.Title);
            }

            if (item.Changes.HasFlag(OutlineItemChanges.Destination))
            {
                if (item.Destination is { } destination)
                {
                    destination.WriteTo(dictionary);
                }
                else
                {
                    dictionary.Remove(PdfNames.Dest);
                    if (dictionary.Get<PdfDictionary>(PdfNames.A) is { } action && action.GetName(PdfNames.S) == PdfNames.GoTo)
                    {
                        dictionary.Remove(PdfNames.A);
                    }
                }
            }

            if (item.Changes.HasFlag(OutlineItemChanges.Style))
            {
                dictionary[PdfNames.C] = item.Color is { } color ? PdfArray.FromNumbers(color.GetComponents()) : null;
                var flags = (item.IsItalic ? 1 : 0) | (item.IsBold ? 2 : 0);
                dictionary[PdfNames.F] = flags != 0 ? PdfInteger.Get(flags) : null;
            }

            visible++;
            if (item.Children.Count > 0)
            {
                var childVisible = item.Children.WriteChildren(document, item.Reference!, dictionary, depth + 1);
                dictionary[PdfNames.First] = item.Children._items[0].Reference;
                dictionary[PdfNames.Last] = item.Children._items[^1].Reference;
                dictionary[PdfNames.Count] = PdfInteger.Get(item.IsOpen ? childVisible : -childVisible);
                if (item.IsOpen)
                {
                    visible += childVisible;
                }
            }
            else
            {
                dictionary.Remove(PdfNames.First);
                dictionary.Remove(PdfNames.Last);
                dictionary.Remove(PdfNames.Count);
            }

            item.Changes = OutlineItemChanges.None;
        }

        parent[PdfNames.First] = _items[0].Reference;
        parent[PdfNames.Last] = _items[^1].Reference;
        return visible;
    }

    private void LoadChildren(PdfDocument document, PdfDictionary node, HashSet<PdfDictionary> visited, ref int count, int depth)
    {
        if (depth > MaxDepth)
            return;

        var current = node[PdfNames.First] as PdfReference;
        while (current is not null && count < MaxItems)
        {
            if (current.Resolve() is not PdfDictionary dictionary || !visited.Add(dictionary))
                break;

            count++;
            var title = dictionary.GetText(PdfNames.Title) ?? "";
            var destination = PdfDestination.Read(document, dictionary[PdfNames.Dest]) ?? ReadGoToAction(document, dictionary);
            PdfColor? color = null;
            if (dictionary.Get<PdfArray>(PdfNames.C) is { Count: 3 } components)
            {
                color = PdfColor.FromRgb(Math.Clamp(components.GetNumber(0), 0, 1), Math.Clamp(components.GetNumber(1), 0, 1), Math.Clamp(components.GetNumber(2), 0, 1));
            }

            var flags = dictionary.GetInteger(PdfNames.F, 0);
            var item = new PdfOutlineItem(title, destination, isOpen: dictionary.GetInteger(PdfNames.Count, 0) > 0, color, isBold: (flags & 2) != 0, isItalic: (flags & 1) != 0, dictionary, current);
            item.Children.LoadChildren(document, dictionary, visited, ref count, depth + 1);
            item.Collection = this;
            _items.Add(item);
            current = dictionary[PdfNames.Next] as PdfReference;
        }
    }

    private static PdfDestination? ReadGoToAction(PdfDocument document, PdfDictionary item)
    {
        if (item.Get<PdfDictionary>(PdfNames.A) is { } action && action.GetName(PdfNames.S) == PdfNames.GoTo)
            return PdfDestination.Read(document, action);

        return null;
    }
}
