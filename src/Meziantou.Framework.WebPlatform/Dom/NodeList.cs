using System.Collections;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-nodelist</summary>
[IdlInterface("NodeList", "https://dom.spec.whatwg.org/#nodelist")]
public abstract class NodeList : IReadOnlyList<Node>
{
    private protected NodeList()
    {
    }

    /// <summary>Gets the number of nodes in the list.</summary>
    [IdlMember("length", "https://dom.spec.whatwg.org/#dom-nodelist-length")]
    public abstract uint Length { get; }

    /// <summary>Returns the node at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-nodelist-item")]
    public abstract Node? Item(uint index);

    // The indexer is renamed in metadata as "Item" is the name of the item() method
    /// <summary>Gets the node at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://dom.spec.whatwg.org/#dom-nodelist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Node? this[uint index] => Item(index);

    /// <summary>Gets the number of nodes in the list.</summary>
    [NotInIdl]
    public int Count => (int)Length;

    /// <summary>Gets the node at the given index; throws when the index is out of range.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Node this[int index] => (index >= 0 ? Item((uint)index) : null) ?? throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    [IdlMember("iterable", "https://dom.spec.whatwg.org/#nodelist")]
    public virtual IEnumerator<Node> GetEnumerator()
    {
        for (uint i = 0; i < Length; i++)
        {
            var node = Item(i);
            if (node is null)
                yield break;

            yield return node;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Live list of the children of a node (<c>node.childNodes</c>).</summary>
internal sealed class ChildNodeList(Node parent) : NodeList
{
    // Cursor to make sequential indexed access O(1)
    private int _cachedVersion = -1;
    private Node? _cachedNode;
    private uint _cachedIndex;

    public override uint Length => (uint)parent.ChildCount;

    public override Node? Item(uint index)
    {
        var count = (uint)parent.ChildCount;
        if (index >= count)
            return null;

        // Start from the closest known position: the first child, the last child, or the cached node
        Node node = parent.FirstChild!;
        var position = 0u;
        var distance = index;
        if (count - 1 - index < distance)
        {
            node = parent.LastChild!;
            position = count - 1;
            distance = count - 1 - index;
        }

        if (_cachedVersion == parent.ChildrenVersion && _cachedNode is not null)
        {
            var cachedDistance = _cachedIndex > index ? _cachedIndex - index : index - _cachedIndex;
            if (cachedDistance < distance)
            {
                node = _cachedNode;
                position = _cachedIndex;
            }
        }

        while (position < index)
        {
            node = node.NextSibling!;
            position++;
        }

        while (position > index)
        {
            node = node.PreviousSibling!;
            position--;
        }

        _cachedVersion = parent.ChildrenVersion;
        _cachedNode = node;
        _cachedIndex = position;
        return node;
    }

    public override IEnumerator<Node> GetEnumerator()
    {
        for (var node = parent.FirstChild; node is not null; node = node.NextSibling)
        {
            yield return node;
        }
    }
}

/// <summary>A static node list (e.g. the result of <c>querySelectorAll</c>).</summary>
internal sealed class StaticNodeList(IReadOnlyList<Node> nodes) : NodeList
{
    public static StaticNodeList Empty { get; } = new([]);

    public override uint Length => (uint)nodes.Count;

    public override Node? Item(uint index) => index < (uint)nodes.Count ? nodes[(int)index] : null;
}

/// <summary>Live list of the descendants of a node that match a filter (e.g. <c>document.getElementsByName()</c>).</summary>
internal sealed class FilteredNodeList(Node root, Func<Node, bool> filter) : NodeList
{
    private readonly List<Node> _nodes = [];
    private int _version = -1;
    private Document? _document;

    private List<Node> Nodes
    {
        get
        {
            var document = root.NodeDocument;
            if (_version != document.DomVersion || !ReferenceEquals(_document, document))
            {
                _nodes.Clear();
                for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
                {
                    if (filter(node))
                    {
                        _nodes.Add(node);
                    }
                }

                _version = document.DomVersion;
                _document = document;
            }

            return _nodes;
        }
    }

    public override uint Length => (uint)Nodes.Count;

    public override Node? Item(uint index)
    {
        var nodes = Nodes;
        return index < (uint)nodes.Count ? nodes[(int)index] : null;
    }
}
