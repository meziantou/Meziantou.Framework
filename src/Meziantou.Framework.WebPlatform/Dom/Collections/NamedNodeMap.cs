using System.Collections;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-namednodemap</summary>
/// <seealso href="https://dom.spec.whatwg.org/#namednodemap"/>
[IdlInterface("NamedNodeMap", "https://dom.spec.whatwg.org/#namednodemap")]
public sealed class NamedNodeMap : IReadOnlyList<Attr>
{
    private readonly Element _element;

    internal NamedNodeMap(Element element)
    {
        _element = element;
    }

    /// <summary>Gets the number of attributes in the map.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-length"/>
    [IdlMember("length", "https://dom.spec.whatwg.org/#dom-namednodemap-length")]
    public uint Length => (uint)_element.AttributeCount;

    /// <summary>Gets the number of attributes in the map.</summary>
    [NotInIdl]
    public int Count => _element.AttributeCount;

    /// <summary>Returns the attribute at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-item"/>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-namednodemap-item")]
    public Attr? Item(uint index) => index < (uint)_element.AttributeCount ? _element.GetAttributeNodeAt((int)index) : null;

    /// <summary>Gets the attribute at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-item"/>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-namednodemap-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Attr? this[uint index] => Item(index);

    /// <summary>Gets the attribute at the given index, and throws when the index is out of range.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Attr this[int index] => Item((uint)index) ?? throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Gets the attribute with the given qualified name, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem"/>
    [IdlMember("getNamedItem", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Attr? this[string qualifiedName] => GetNamedItem(qualifiedName);

    /// <summary>Returns the attribute with the given qualified name, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem"/>
    [IdlMember("getNamedItem", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem")]
    public Attr? GetNamedItem(string qualifiedName) => _element.GetAttributeNode(qualifiedName);

    /// <summary>Returns the attribute with the given namespace and local name, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-getnameditemns"/>
    [IdlMember("getNamedItemNS", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditemns")]
    public Attr? GetNamedItemNS(string? @namespace, string localName) => _element.GetAttributeNodeNS(@namespace, localName);

    /// <summary>Adds the attribute to the element and returns the attribute it replaced, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-setnameditem"/>
    [IdlMember("setNamedItem", "https://dom.spec.whatwg.org/#dom-namednodemap-setnameditem")]
    public Attr? SetNamedItem(Attr attr) => _element.SetAttributeNodeCore(attr);

    /// <summary>Adds the attribute to the element and returns the attribute it replaced, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-setnameditemns"/>
    [IdlMember("setNamedItemNS", "https://dom.spec.whatwg.org/#dom-namednodemap-setnameditemns")]
    public Attr? SetNamedItemNS(Attr attr) => _element.SetAttributeNodeCore(attr);

    /// <summary>https://dom.spec.whatwg.org/#dom-namednodemap-removenameditem</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-removenameditem"/>
    [IdlMember("removeNamedItem", "https://dom.spec.whatwg.org/#dom-namednodemap-removenameditem")]
    public Attr RemoveNamedItem(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var index = _element.FindAttributeByQualifiedName(qualifiedName);
        if (index < 0)
            throw DomException.NotFound($"There is no attribute named '{qualifiedName}'");

        return _element.RemoveAttributeAt(index, createNode: true)!;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-namednodemap-removenameditemns</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-namednodemap-removenameditemns"/>
    [IdlMember("removeNamedItemNS", "https://dom.spec.whatwg.org/#dom-namednodemap-removenameditemns")]
    public Attr RemoveNamedItemNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        var index = _element.FindAttribute(@namespace, localName);
        if (index < 0)
            throw DomException.NotFound($"There is no attribute named '{localName}'");

        return _element.RemoveAttributeAt(index, createNode: true)!;
    }

    /// <summary>https://dom.spec.whatwg.org/#ref-for-dfn-supported-property-names%E2%91%A0</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames()
    {
        var names = new List<string>();
        for (var i = 0; i < _element.AttributeCount; i++)
        {
            var name = _element.GetAttributeQualifiedName(i);
            if (names.Contains(name))
                continue;

            if (_element.IsHtmlNamespace && _element.NodeDocument.IsHtmlDocument && name.AsSpan().IndexOfAnyInRange('A', 'Z') >= 0)
                continue;

            names.Add(name);
        }

        return names;
    }

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<Attr> GetEnumerator()
    {
        for (var i = 0; i < _element.AttributeCount; i++)
        {
            yield return _element.GetAttributeNodeAt(i);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
