using System.Collections;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#domstringlist</summary>
[IdlInterface("DOMStringList", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#domstringlist")]
public sealed class DomStringList : IReadOnlyList<string>
{
    private readonly string[] _items;

    /// <summary>Creates a list that contains a copy of the given strings.</summary>
    [NotInIdl]
    public DomStringList(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <summary>Gets the number of strings in the list.</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-domstringlist-length")]
    public uint Length => (uint)_items.Length;

    /// <summary>Gets the number of strings in the list.</summary>
    [NotInIdl]
    public int Count => _items.Length;

    /// <summary>Returns the string at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-domstringlist-item")]
    public string? Item(uint index) => index < (uint)_items.Length ? _items[index] : null;

    /// <summary>Gets the string at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-domstringlist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string? this[uint index] => Item(index);

    /// <summary>Gets the string at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string this[int index] => _items[index];

    /// <summary>Returns whether the list contains the given string.</summary>
    [IdlMember("contains", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-domstringlist-contains")]
    public bool Contains(string value) => Array.IndexOf(_items, value) >= 0;

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
