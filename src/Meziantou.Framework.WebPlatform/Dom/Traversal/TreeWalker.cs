namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-treewalker</summary>
[IdlInterface("TreeWalker", "https://dom.spec.whatwg.org/#treewalker")]
public sealed class TreeWalker
{
    private readonly Traverser _traverser;
    private Node _current;

    internal TreeWalker(Node root, uint whatToShow, INodeFilter? filter)
    {
        _traverser = new Traverser(root, whatToShow, filter);
        _current = root;
    }

    /// <summary>Gets the root node of the traversal.</summary>
    [IdlMember("root", "https://dom.spec.whatwg.org/#dom-treewalker-root")]
    public Node Root => _traverser.Root;

    /// <summary>Gets the bit mask of the node types the walker presents.</summary>
    [IdlMember("whatToShow", "https://dom.spec.whatwg.org/#dom-treewalker-whattoshow")]
    public uint WhatToShow => _traverser.WhatToShow;

    /// <summary>Gets the filter used to accept, reject or skip nodes, or null when there is none.</summary>
    [IdlMember("filter", "https://dom.spec.whatwg.org/#dom-treewalker-filter")]
    public INodeFilter? Filter => _traverser.Filter;

    /// <summary>Gets or sets the node the walker is currently positioned at.</summary>
    [IdlMember("currentNode", "https://dom.spec.whatwg.org/#dom-treewalker-currentnode")]
    public Node CurrentNode
    {
        get => _current;
        set => _current = value ?? throw new TypeErrorException("The current node cannot be null");
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-treewalker-parentnode</summary>
    [IdlMember("parentNode", "https://dom.spec.whatwg.org/#dom-treewalker-parentnode")]
    public Node? ParentNode()
    {
        Node? node = _current;
        while (node is not null && !ReferenceEquals(node, Root))
        {
            node = node.ParentNode;
            if (node is not null && _traverser.FilterNode(node) == NodeFilter.FilterAccept)
            {
                _current = node;
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Moves to the first visible child of the current node and returns it, or null when there is none.
    /// </summary>
    [IdlMember("firstChild", "https://dom.spec.whatwg.org/#dom-treewalker-firstchild")]
    public Node? FirstChild() => TraverseChildren(first: true);

    /// <summary>
    /// Moves to the last visible child of the current node and returns it, or null when there is none.
    /// </summary>
    [IdlMember("lastChild", "https://dom.spec.whatwg.org/#dom-treewalker-lastchild")]
    public Node? LastChild() => TraverseChildren(first: false);

    /// <summary>https://dom.spec.whatwg.org/#concept-traverse-children</summary>
    private Node? TraverseChildren(bool first)
    {
        var node = first ? _current.FirstChild : _current.LastChild;
        while (node is not null)
        {
            var result = _traverser.FilterNode(node);
            if (result == NodeFilter.FilterAccept)
            {
                _current = node;
                return node;
            }

            if (result == NodeFilter.FilterSkip)
            {
                var child = first ? node.FirstChild : node.LastChild;
                if (child is not null)
                {
                    node = child;
                    continue;
                }
            }

            while (node is not null)
            {
                var sibling = first ? node.NextSibling : node.PreviousSibling;
                if (sibling is not null)
                {
                    node = sibling;
                    break;
                }

                var parent = node.ParentNode;
                if (parent is null || ReferenceEquals(parent, Root) || ReferenceEquals(parent, _current))
                    return null;

                node = parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Moves to the next visible sibling of the current node and returns it, or null when there is none.
    /// </summary>
    [IdlMember("nextSibling", "https://dom.spec.whatwg.org/#dom-treewalker-nextsibling")]
    public Node? NextSibling() => TraverseSiblings(next: true);

    /// <summary>
    /// Moves to the previous visible sibling of the current node and returns it, or null when there is none.
    /// </summary>
    [IdlMember("previousSibling", "https://dom.spec.whatwg.org/#dom-treewalker-previoussibling")]
    public Node? PreviousSibling() => TraverseSiblings(next: false);

    /// <summary>https://dom.spec.whatwg.org/#concept-traverse-siblings</summary>
    private Node? TraverseSiblings(bool next)
    {
        var node = _current;
        if (ReferenceEquals(node, Root))
            return null;

        while (true)
        {
            var sibling = next ? node.NextSibling : node.PreviousSibling;
            while (sibling is not null)
            {
                node = sibling;
                var result = _traverser.FilterNode(node);
                if (result == NodeFilter.FilterAccept)
                {
                    _current = node;
                    return node;
                }

                sibling = next ? node.FirstChild : node.LastChild;
                if (result == NodeFilter.FilterReject || sibling is null)
                {
                    sibling = next ? node.NextSibling : node.PreviousSibling;
                }
            }

            node = node.ParentNode;
            if (node is null || ReferenceEquals(node, Root))
                return null;

            if (_traverser.FilterNode(node) == NodeFilter.FilterAccept)
                return null;
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-treewalker-previousnode</summary>
    [IdlMember("previousNode", "https://dom.spec.whatwg.org/#dom-treewalker-previousnode")]
    public Node? PreviousNode()
    {
        var node = _current;
        while (!ReferenceEquals(node, Root))
        {
            var sibling = node.PreviousSibling;
            while (sibling is not null)
            {
                node = sibling;
                var result = _traverser.FilterNode(node);
                while (result != NodeFilter.FilterReject && node.LastChild is not null)
                {
                    node = node.LastChild;
                    result = _traverser.FilterNode(node);
                }

                if (result == NodeFilter.FilterAccept)
                {
                    _current = node;
                    return node;
                }

                sibling = node.PreviousSibling;
            }

            if (ReferenceEquals(node, Root) || node.ParentNode is null)
                return null;

            node = node.ParentNode;
            if (_traverser.FilterNode(node) == NodeFilter.FilterAccept)
            {
                _current = node;
                return node;
            }
        }

        return null;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-treewalker-nextnode</summary>
    [IdlMember("nextNode", "https://dom.spec.whatwg.org/#dom-treewalker-nextnode")]
    public Node? NextNode()
    {
        var node = _current;
        var result = NodeFilter.FilterAccept;
        while (true)
        {
            while (result != NodeFilter.FilterReject && node.FirstChild is not null)
            {
                node = node.FirstChild;
                result = _traverser.FilterNode(node);
                if (result == NodeFilter.FilterAccept)
                {
                    _current = node;
                    return node;
                }
            }

            Node? sibling = null;
            Node? temporary = node;
            while (temporary is not null)
            {
                if (ReferenceEquals(temporary, Root))
                    return null;

                sibling = temporary.NextSibling;
                if (sibling is not null)
                    break;

                temporary = temporary.ParentNode;
            }

            if (sibling is null)
                return null;

            node = sibling;
            result = _traverser.FilterNode(node);
            if (result == NodeFilter.FilterAccept)
            {
                _current = node;
                return node;
            }
        }
    }
}
