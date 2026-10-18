namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Maps the IDs to the elements of a tree whose root is a document or a shadow root, so that getElementById() does not
/// have to search the tree (https://dom.spec.whatwg.org/#dom-nonelementparentnode-getelementbyid). The map is created on
/// the first lookup (so parsing a document does not pay for it), then updated by the insert, remove and move algorithms
/// and when an id attribute changes.
/// </summary>
internal sealed class IdMap
{
    // An Element, or a List<Element> when several elements have the same ID (in no particular order)
    private readonly Dictionary<string, object> _map = new(StringComparer.Ordinal);

    public void Add(string id, Element element)
    {
        if (!_map.TryGetValue(id, out var value))
        {
            _map[id] = element;
        }
        else if (value is Element other)
        {
            _map[id] = new List<Element>(2) { other, element };
        }
        else
        {
            ((List<Element>)value).Add(element);
        }
    }

    public void Remove(string id, Element element)
    {
        if (!_map.TryGetValue(id, out var value))
            return;

        if (value is Element)
        {
            if (ReferenceEquals(value, element))
            {
                _map.Remove(id);
            }

            return;
        }

        var list = (List<Element>)value;
        list.Remove(element);
        if (list.Count == 1)
        {
            _map[id] = list[0];
        }
    }

    /// <summary>Returns the first element in tree order with the given ID.</summary>
    public Element? Get(string id)
    {
        if (!_map.TryGetValue(id, out var value))
            return null;

        if (value is Element element)
            return element;

        var list = (List<Element>)value;
        var first = list[0];
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].IsPreceding(first))
            {
                first = list[i];
            }
        }

        return first;
    }

    /// <summary>Returns the number of elements with the given ID, and the element when there is a single one.</summary>
    public int GetCount(string id, out Element? single)
    {
        single = null;
        if (!_map.TryGetValue(id, out var value))
            return 0;

        if (value is Element element)
        {
            single = element;
            return 1;
        }

        return ((List<Element>)value).Count;
    }

    /// <summary>Adds the elements with the given ID to the list, in no particular order.</summary>
    public void AddAll(string id, List<Element> result)
    {
        if (!_map.TryGetValue(id, out var value))
            return;

        if (value is Element element)
        {
            result.Add(element);
        }
        else
        {
            result.AddRange((List<Element>)value);
        }
    }

    /// <summary>Returns the existing ID map of the root of the node's tree, when the root is a document or a shadow root.</summary>
    public static IdMap? GetForRoot(Node root) => root switch
    {
        Document document => document.IdMap,
        ShadowRoot shadowRoot => shadowRoot.IdMap,
        _ => null,
    };

    /// <summary>Returns the ID map of a document or a shadow root, building it from the tree on first use.</summary>
    public static IdMap? GetOrCreateForRoot(Node root)
    {
        if (root is not (Document or ShadowRoot))
            return null;

        var existing = GetForRoot(root);
        if (existing is not null)
            return existing;

        var map = new IdMap();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element { IdValue: { } id } element)
            {
                map.Add(id, element);
            }
        }

        if (root is Document document)
        {
            document.IdMap = map;
        }
        else
        {
            ((ShadowRoot)root).IdMap = map;
        }

        root.NodeDocument.HasIdMaps = true;
        return map;
    }

    /// <summary>Adds (or removes) the elements with an ID of the subtree rooted at <paramref name="node"/> (shadow trees excluded: they are other tree scopes).</summary>
    public static void UpdateSubtree(Node parent, Node node, bool add)
    {
        if (!parent.NodeDocument.HasIdMaps)
            return;

        IdMap? map = null;
        var resolved = false;
        for (var current = node; current is not null; current = current.GetNextInTreeOrder(node))
        {
            if (current is not Element { IdValue: { } id } element)
            {
                if (ReferenceEquals(current, node) && current.FirstChild is null)
                    return;

                continue;
            }

            if (!resolved)
            {
                map = GetForRoot(parent.GetRoot());
                resolved = true;
            }

            if (map is null)
                return;

            if (add)
            {
                map.Add(id, element);
            }
            else
            {
                map.Remove(id, element);
            }
        }
    }
}
