using System.Collections;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-htmlcollection</summary>
[IdlInterface("HTMLCollection", "https://dom.spec.whatwg.org/#htmlcollection")]
public class HtmlCollection : IReadOnlyList<Element>, ISupportedPropertyNames
{
    private readonly Node _root;
    private readonly Func<Element, bool> _filter;
    private readonly bool _childrenOnly;
    private readonly bool _structural;
    private List<Element>? _cache;
    private Document? _cacheDocument;
    private int _cacheVersion = -1;

    private readonly Action<List<Element>>? _populate;

    /// <param name="root">The root of the collection.</param>
    /// <param name="filter">Selects the elements of the collection.</param>
    /// <param name="childrenOnly">Whether the elements are the children of the root, else its descendants.</param>
    /// <param name="structural">
    /// Whether the filter only depends on the names of the elements, which never change: the collection is then only
    /// recomputed when nodes are inserted or removed, not when an attribute or a text changes.
    /// </param>
    internal HtmlCollection(Node root, Func<Element, bool> filter, bool childrenOnly = false, bool structural = false)
    {
        _root = root;
        _filter = filter;
        _childrenOnly = childrenOnly;
        _structural = structural;
    }

    /// <summary>Creates a collection whose elements are computed by <paramref name="populate"/> (e.g. the rows of a table).</summary>
    internal HtmlCollection(Node root, Action<List<Element>> populate)
    {
        _root = root;
        _filter = static _ => true;
        _populate = populate;
    }

    /// <summary>The elements of the collection, recomputed when the tree changed.</summary>
    private protected List<Element> Elements
    {
        get
        {
            var document = _root.NodeDocument;
            var version = !_structural || _populate is not null ? document.DomVersion : _childrenOnly ? _root.ChildrenVersion : document.TreeVersion;
            if (_cache is not null && _cacheVersion == version && ReferenceEquals(_cacheDocument, document))
                return _cache;

            var result = _cache ?? [];
            result.Clear();
            if (_populate is not null)
            {
                _populate(result);
            }
            else if (_childrenOnly)
            {
                for (var node = _root.FirstChild; node is not null; node = node.NextSibling)
                {
                    if (node is Element element && _filter(element))
                    {
                        result.Add(element);
                    }
                }
            }
            else
            {
                for (var node = _root.FirstChild; node is not null; node = node.GetNextInTreeOrder(_root))
                {
                    if (node is Element element && _filter(element))
                    {
                        result.Add(element);
                    }
                }
            }

            _cache = result;
            _cacheDocument = document;
            _cacheVersion = version;
            return result;
        }
    }

    /// <summary>Gets the number of elements in the collection.</summary>
    [IdlMember("length", "https://dom.spec.whatwg.org/#dom-htmlcollection-length")]
    public uint Length => (uint)Elements.Count;

    /// <summary>Gets the number of elements in the collection.</summary>
    [NotInIdl]
    public int Count => Elements.Count;

    /// <summary>Returns the element at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-htmlcollection-item")]
    public Element? Item(uint index)
    {
        var elements = Elements;
        return index < (uint)elements.Count ? elements[(int)index] : null;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-htmlcollection-nameditem-key</summary>
    [IdlMember("namedItem", "https://dom.spec.whatwg.org/#dom-htmlcollection-nameditem")]
    public virtual Element? NamedItem(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
            return null;

        foreach (var element in Elements)
        {
            if (element.IdValue == key || (element.IsHtmlNamespace && element.GetAttributeValue("name") == key))
                return element;
        }

        return null;
    }

    /// <summary>Gets the element at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-htmlcollection-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Element? this[uint index] => Item(index);

    /// <summary>Gets the element at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Element this[int index] => Elements[index];

    /// <summary>Gets the first element with the given <c>id</c> or <c>name</c>, or null when there is none.</summary>
    [IdlMember("namedItem", "https://dom.spec.whatwg.org/#dom-htmlcollection-nameditem")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Element? this[string name] => NamedItem(name);

    /// <summary>https://dom.spec.whatwg.org/#ref-for-dfn-supported-property-names</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames()
    {
        var result = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in Elements)
        {
            if (element.IdValue is { } id && names.Add(id))
            {
                result.Add(id);
            }

            if (element.IsHtmlNamespace && element.GetAttributeValue("name") is { Length: > 0 } name && names.Add(name))
            {
                result.Add(name);
            }
        }

        return result;
    }

    bool ISupportedPropertyNames.IsSupportedPropertyName(string name)
    {
        foreach (var element in Elements)
        {
            if (element.IdValue == name || (name.Length > 0 && element.IsHtmlNamespace && element.GetAttributeValue("name") == name))
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<Element> GetEnumerator() => ((IEnumerable<Element>)Elements.ToArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>https://dom.spec.whatwg.org/#concept-getelementsbytagname</summary>
    internal static HtmlCollection ByQualifiedName(Node root, string qualifiedName)
    {
        if (qualifiedName == "*")
            return new HtmlCollection(root, static _ => true, structural: true);

        if (root.NodeDocument.IsHtmlDocument)
        {
            var lowercase = InfraAscii.ToAsciiLowercase(qualifiedName);
            return new HtmlCollection(root, element => element.IsHtmlNamespace ? element.QualifiedName == lowercase : element.QualifiedName == qualifiedName, structural: true);
        }

        return new HtmlCollection(root, element => element.QualifiedName == qualifiedName, structural: true);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-getelementsbytagnamens</summary>
    internal static HtmlCollection ByNamespaceAndLocalName(Node root, string? @namespace, string localName)
    {
        if (@namespace is { Length: 0 })
        {
            @namespace = null;
        }

        return (@namespace, localName) switch
        {
            ("*", "*") => new HtmlCollection(root, static _ => true, structural: true),
            ("*", _) => new HtmlCollection(root, element => element.LocalName == localName, structural: true),
            (_, "*") => new HtmlCollection(root, element => element.NamespaceUri == @namespace, structural: true),
            _ => new HtmlCollection(root, element => element.LocalName == localName && element.NamespaceUri == @namespace, structural: true),
        };
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-getelementsbyclassname</summary>
    internal static HtmlCollection ByClassNames(Node root, string classNames)
    {
        var classes = OrderedSets.Parse(classNames);
        if (classes.Count == 0)
            return new HtmlCollection(root, static _ => false);

        var quirks = root.NodeDocument.Mode == DocumentMode.Quirks;
        return new HtmlCollection(root, element =>
        {
            var elementClasses = element.GetClassNames();
            foreach (var className in classes)
            {
                var found = false;
                foreach (var elementClass in elementClasses)
                {
                    if (quirks ? InfraAscii.EqualsIgnoreAsciiCase(elementClass, className) : elementClass == className)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return false;
            }

            return true;
        });
    }
}
