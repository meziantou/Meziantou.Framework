using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Maps the supported property names of a document to its named elements
/// (https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem-filter), so that the named property getter of
/// the document, which the bindings consult on each property access (<c>document.body</c>...), does not have to search
/// the tree. The map is created on the first property access of a document that has candidate elements
/// (<see cref="Document.HasNamedItemCandidates"/>), then updated by the insert, remove and move algorithms and when a
/// name or an id attribute of a candidate element changes.
/// </summary>
internal sealed class DocumentNamedItemMap
{
    // An Element, or a List<Element> when several elements have the same name (in no particular order)
    private readonly Dictionary<string, object> _map = new(StringComparer.Ordinal);

    // The collections returned for the names of several elements: the same object is returned each time
    private Dictionary<string, HtmlCollection>? _collections;

    /// <summary>Whether the element is of a type that can be a named element of its document.</summary>
    public static bool IsCandidate(Element element) => element.IsHtmlNamespace && element.TagId is TagId.Embed or TagId.Form or TagId.Iframe or TagId.Img or TagId.Object;

    /// <summary>
    /// Returns the names of a candidate element: the value of its name attribute, and the value of its id attribute when
    /// it is an object element, or an img element that also has a non-empty name attribute.
    /// </summary>
    public static void GetNames(Element element, string? id, string? name, out string? idName, out string? nameName)
    {
        nameName = string.IsNullOrEmpty(name) ? null : name;
        idName = !string.IsNullOrEmpty(id) && (element.TagId == TagId.Object || (element.TagId == TagId.Img && nameName is not null)) ? id : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem-filter</summary>
    public static bool IsNamedElement(Element element, string name)
    {
        if (name.Length == 0 || !IsCandidate(element))
            return false;

        GetNames(element, element.IdValue, element.GetAttributeValue("name"), out var idName, out var nameName);
        return idName == name || nameName == name;
    }

    public bool Contains(string name) => _map.ContainsKey(name);

    /// <summary>Returns the number of named elements with the given name, and the element when there is a single one.</summary>
    public int GetCount(string name, out Element? single)
    {
        single = null;
        if (!_map.TryGetValue(name, out var value))
            return 0;

        if (value is Element element)
        {
            single = element;
            return 1;
        }

        return ((List<Element>)value).Count;
    }

    /// <summary>Returns the collection of the named elements with the given name (the same object for a name, as long as the name is supported).</summary>
    public HtmlCollection GetCollection(Document document, string name)
    {
        _collections ??= new Dictionary<string, HtmlCollection>(StringComparer.Ordinal);
        if (!_collections.TryGetValue(name, out var collection))
        {
            collection = new HtmlCollection(document, element => IsNamedElement(element, name));
            _collections[name] = collection;
        }

        return collection;
    }

    private void Add(string name, Element element)
    {
        if (!_map.TryGetValue(name, out var value))
        {
            _map[name] = element;
        }
        else if (value is Element other)
        {
            _map[name] = new List<Element>(2) { other, element };
        }
        else
        {
            ((List<Element>)value).Add(element);
        }
    }

    private void Remove(string name, Element element)
    {
        if (!_map.TryGetValue(name, out var value))
            return;

        if (value is Element)
        {
            if (ReferenceEquals(value, element))
            {
                _map.Remove(name);
                _collections?.Remove(name);
            }

            return;
        }

        var list = (List<Element>)value;
        list.Remove(element);
        if (list.Count == 1)
        {
            _map[name] = list[0];
        }
    }

    private void Update(Element element, string? id, string? name, bool add)
    {
        GetNames(element, id, name, out var idName, out var nameName);
        if (idName is not null)
        {
            if (add)
            {
                Add(idName, element);
            }
            else
            {
                Remove(idName, element);
            }
        }

        // An element whose id and name are the same is a single named element with that name
        if (nameName is not null && nameName != idName)
        {
            if (add)
            {
                Add(nameName, element);
            }
            else
            {
                Remove(nameName, element);
            }
        }
    }

    /// <summary>Returns the map of the document, building it from the tree on first use.</summary>
    public static DocumentNamedItemMap GetOrCreate(Document document)
    {
        if (document.NamedItemMap is { } existing)
            return existing;

        var map = new DocumentNamedItemMap();
        for (var node = document.FirstChild; node is not null; node = node.GetNextInTreeOrder(document))
        {
            if (node is Element element && IsCandidate(element))
            {
                map.Update(element, element.IdValue, element.GetAttributeValue("name"), add: true);
            }
        }

        document.NamedItemMap = map;
        return map;
    }

    /// <summary>Adds (or removes) the named elements of the subtree rooted at <paramref name="node"/> (shadow trees excluded: their elements are not in the document tree).</summary>
    public static void UpdateSubtree(Node parent, Node node, bool add)
    {
        if (parent.NodeDocument.NamedItemMap is not { } map || !(parent is Document || parent.IsInDocumentTree))
            return;

        for (var current = node; current is not null; current = current.GetNextInTreeOrder(node))
        {
            if (current is Element element && IsCandidate(element))
            {
                map.Update(element, element.IdValue, element.GetAttributeValue("name"), add);
            }
        }
    }

    /// <summary>Updates the names of a candidate element whose name or id attribute changed (the attribute already has its new value).</summary>
    public static void OnAttributeChanged(Element element, string localName, string? oldValue, string? newValue)
    {
        var document = element.NodeDocument;
        if (newValue is not null && (localName == "name" || element.TagId == TagId.Object))
        {
            document.HasNamedItemCandidates = true;
        }

        if (document.NamedItemMap is not { } map || !element.IsInDocumentTree)
            return;

        var isName = localName == "name";
        var id = element.IdValue;
        var name = element.GetAttributeValue("name");
        map.Update(element, isName ? id : oldValue, isName ? oldValue : name, add: false);
        map.Update(element, id, name, add: true);
    }
}
