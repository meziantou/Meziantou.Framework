using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>An array: an ordered sequence of objects (ISO 32000-2, 7.3.6). It is mutable and not thread-safe.</summary>
public sealed class PdfArray : PdfObject, IList<PdfObject>, IReadOnlyList<PdfObject>
{
    private readonly List<PdfObject> _items;

    /// <summary>Initializes a new instance of the <see cref="PdfArray"/> class.</summary>
    public PdfArray()
    {
        _items = [];
    }

    /// <summary>Initializes a new instance of the <see cref="PdfArray"/> class with items.</summary>
    /// <param name="items">The items.</param>
    public PdfArray(params IEnumerable<PdfObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        foreach (var item in _items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));
        }
    }

    private PdfArray(List<PdfObject> items, bool wrap)
    {
        Debug.Assert(wrap);
        _items = items;
    }

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Array;

    /// <inheritdoc cref="ICollection{T}.Count"/>
    public int Count => _items.Count;

    bool ICollection<PdfObject>.IsReadOnly => false;

    /// <summary>Gets or sets the item at an index. The item is not resolved: it may be a <see cref="PdfReference"/>.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The item.</returns>
    public PdfObject this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _items[index] = value;
        }
    }

    /// <summary>Creates an array of numbers.</summary>
    /// <param name="values">The numbers.</param>
    /// <returns>The array: integers for the values that are whole numbers, real numbers for the others.</returns>
    public static PdfArray FromNumbers(params ReadOnlySpan<double> values)
    {
        var items = new List<PdfObject>(values.Length);
        foreach (var value in values)
        {
            items.Add(PdfObjectFactory.Number(value));
        }

        return new PdfArray(items, wrap: true);
    }

    /// <summary>Creates an array that uses a list as its storage, without copying it.</summary>
    internal static PdfArray Wrap(List<PdfObject> items) => new(items, wrap: true);

    /// <summary>Gets the item at an index, resolving it when it is a reference.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The item, or <see cref="PdfNull.Instance"/> for a reference to an object that does not exist.</returns>
    public PdfObject GetResolved(int index) => PdfReference.ResolveIfReference(_items[index]);

    /// <summary>Gets the item at an index, resolved, when it has a type.</summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="value">The item.</param>
    /// <returns><see langword="true"/> when the index is in the array and the resolved item has the type.</returns>
    public bool TryGetResolved<T>(int index, [NotNullWhen(true)] out T? value)
        where T : PdfObject
    {
        if ((uint)index < (uint)_items.Count && GetResolved(index) is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Gets the item at an index as a number.</summary>
    /// <param name="index">The index.</param>
    /// <param name="defaultValue">The value when the item is not a number or the index is out of the array.</param>
    /// <returns>The number.</returns>
    public double GetNumber(int index, double defaultValue = 0) => TryGetResolved<PdfNumber>(index, out var number) ? number.DoubleValue : defaultValue;

    /// <summary>Adds an item.</summary>
    /// <param name="item">The item.</param>
    public void Add(PdfObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    /// <summary>Inserts an item.</summary>
    /// <param name="index">The index of the item.</param>
    /// <param name="item">The item.</param>
    public void Insert(int index, PdfObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
    }

    /// <inheritdoc/>
    public void Clear() => _items.Clear();

    /// <inheritdoc/>
    public bool Contains(PdfObject item) => _items.Contains(item);

    /// <inheritdoc/>
    public void CopyTo(PdfObject[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public int IndexOf(PdfObject item) => _items.IndexOf(item);

    /// <inheritdoc/>
    public bool Remove(PdfObject item) => _items.Remove(item);

    /// <inheritdoc/>
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <summary>Returns an enumerator over the items, which are not resolved.</summary>
    /// <returns>The enumerator.</returns>
    public List<PdfObject>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<PdfObject> IEnumerable<PdfObject>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
