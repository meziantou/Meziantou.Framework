using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A dictionary: entries whose keys are names (ISO 32000-2, 7.3.7). It keeps the order of its entries, and is mutable and not thread-safe.</summary>
/// <remarks>
/// The indexer gets the values as they are stored: a value may be a <see cref="PdfReference"/>. The <c>Get*</c> and
/// <c>TryGetResolved</c> methods resolve them, and treat a null value as an absent entry, as the specification does.
/// </remarks>
public sealed class PdfDictionary : PdfObject, IEnumerable<KeyValuePair<PdfName, PdfObject>>
{
    private readonly OrderedDictionary<PdfName, PdfObject> _entries;

    /// <summary>Initializes a new instance of the <see cref="PdfDictionary"/> class.</summary>
    public PdfDictionary()
    {
        _entries = [];
    }

    /// <summary>Initializes a new instance of the <see cref="PdfDictionary"/> class with a capacity.</summary>
    /// <param name="capacity">The initial capacity.</param>
    public PdfDictionary(int capacity)
    {
        _entries = new OrderedDictionary<PdfName, PdfObject>(capacity);
    }

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Dictionary;

    /// <summary>Gets the number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the keys, in the order of the entries.</summary>
    public IEnumerable<PdfName> Keys => _entries.Keys;

    /// <summary>Gets or sets the value of an entry, as it is stored: it may be a <see cref="PdfReference"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/> when there is no entry. Setting <see langword="null"/> removes the entry.</returns>
    public PdfObject? this[PdfName key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return _entries.TryGetValue(key, out var value) ? value : null;
        }

        set
        {
            ArgumentNullException.ThrowIfNull(key);
            if (value is null)
            {
                _entries.Remove(key);
            }
            else
            {
                _entries[key] = value;
            }
        }
    }

    /// <summary>Indicates whether the dictionary has an entry.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when there is an entry, whatever its value.</returns>
    public bool ContainsKey(PdfName key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.ContainsKey(key);
    }

    /// <summary>Removes an entry.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the entry existed.</returns>
    public bool Remove(PdfName key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.Remove(key);
    }

    /// <summary>Removes every entry.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>Gets the value of an entry, resolving it when it is a reference.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/> when there is no entry, when the value is null, or when it is a reference to an object that does not exist.</returns>
    public PdfObject? GetResolved(PdfName key)
    {
        var value = this[key];
        if (value is null)
            return null;

        var resolved = PdfReference.ResolveIfReference(value);
        return resolved is PdfNull ? null : resolved;
    }

    /// <summary>Gets the value of an entry, resolved, when it has a type.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the resolved value has the type.</returns>
    public bool TryGetResolved<T>(PdfName key, [NotNullWhen(true)] out T? value)
        where T : PdfObject
    {
        if (GetResolved(key) is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Gets the resolved value of an entry when it has a type.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/> when there is no entry or when it has another type.</returns>
    public T? Get<T>(PdfName key)
        where T : PdfObject => GetResolved(key) as T;

    /// <summary>Gets the value of an entry as a number.</summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The value when there is no number.</param>
    /// <returns>The number.</returns>
    public double GetNumber(PdfName key, double defaultValue = 0) => TryGetResolved<PdfNumber>(key, out var number) ? number.DoubleValue : defaultValue;

    /// <summary>Gets the value of an entry as an integer. A real number is truncated.</summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The value when there is no number.</param>
    /// <returns>The integer.</returns>
    public long GetInteger(PdfName key, long defaultValue = 0)
    {
        return GetResolved(key) switch
        {
            PdfInteger integer => integer.Value,
            PdfReal real when real.Value is >= long.MinValue and <= long.MaxValue => (long)real.Value,
            _ => defaultValue,
        };
    }

    /// <summary>Gets the value of an entry as a boolean.</summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The value when there is no boolean.</param>
    /// <returns>The boolean.</returns>
    public bool GetBoolean(PdfName key, bool defaultValue = false) => TryGetResolved<PdfBoolean>(key, out var value) ? value.Value : defaultValue;

    /// <summary>Gets the value of an entry as a name.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The name, or <see langword="null"/> when the value is not a name.</returns>
    public PdfName? GetName(PdfName key) => Get<PdfName>(key);

    /// <summary>Gets the value of an entry as a text string (ISO 32000-2, 7.9.2.2).</summary>
    /// <param name="key">The key.</param>
    /// <returns>The text, or <see langword="null"/> when the value is not a string.</returns>
    public string? GetText(PdfName key) => Get<PdfString>(key)?.ToText();

    /// <summary>Sets an entry to a name.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The name.</param>
    public void SetName(PdfName key, string value) => this[key] = PdfName.Create(value);

    /// <summary>Sets an entry to a number: an integer when the value is a whole number, a real number otherwise.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The number.</param>
    public void SetNumber(PdfName key, double value) => this[key] = PdfObjectFactory.Number(value);

    /// <summary>Sets an entry to a text string (ISO 32000-2, 7.9.2.2), or removes it when the text is <see langword="null"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The text.</param>
    public void SetText(PdfName key, string? value) => this[key] = value is null ? null : PdfString.FromText(value);

    /// <summary>Indicates whether the value of the <c>/Type</c> entry is a name.</summary>
    /// <param name="type">The name.</param>
    /// <returns><see langword="true"/> when the type is the name.</returns>
    public bool IsType(PdfName type) => GetName(PdfNames.Type) == type;

    /// <summary>Returns an enumerator over the entries, whose values are not resolved.</summary>
    /// <returns>The enumerator.</returns>
    public OrderedDictionary<PdfName, PdfObject>.Enumerator GetEnumerator() => _entries.GetEnumerator();

    IEnumerator<KeyValuePair<PdfName, PdfObject>> IEnumerable<KeyValuePair<PdfName, PdfObject>>.GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

    /// <summary>Copies the entries of the dictionary to a new dictionary. The values are not copied.</summary>
    /// <returns>The copy.</returns>
    public PdfDictionary CloneShallow()
    {
        var copy = new PdfDictionary(_entries.Count);
        foreach (var entry in _entries)
        {
            copy._entries.Add(entry.Key, entry.Value);
        }

        return copy;
    }

    /// <summary>Adds an entry while parsing: the last of duplicate keys wins.</summary>
    internal void SetParsed(PdfName key, PdfObject value) => _entries[key] = value;
}
