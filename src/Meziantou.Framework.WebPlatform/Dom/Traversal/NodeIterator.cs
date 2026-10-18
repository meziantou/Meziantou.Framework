namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-nodeiterator</summary>
[IdlInterface("NodeIterator", "https://dom.spec.whatwg.org/#nodeiterator")]
public sealed class NodeIterator
{
    private readonly Traverser _traverser;
    private (Node Node, bool PointerBefore) _reference;
    private (Node Node, bool PointerBefore)? _candidateReference;

    internal NodeIterator(Node root, uint whatToShow, INodeFilter? filter)
    {
        _traverser = new Traverser(root, whatToShow, filter);
        _reference = (root, true);
        root.NodeDocument.RegisterNodeIterator(this);
    }

    /// <summary>Gets the root node of the traversal.</summary>
    [IdlMember("root", "https://dom.spec.whatwg.org/#dom-nodeiterator-root")]
    public Node Root => _traverser.Root;

    /// <summary>Gets the node the iterator is currently positioned at.</summary>
    [IdlMember("referenceNode", "https://dom.spec.whatwg.org/#dom-nodeiterator-referencenode")]
    public Node ReferenceNode => _reference.Node;

    /// <summary>Gets a value indicating whether the iterator is positioned before the reference node.</summary>
    [IdlMember("pointerBeforeReferenceNode", "https://dom.spec.whatwg.org/#dom-nodeiterator-pointerbeforereferencenode")]
    public bool PointerBeforeReferenceNode => _reference.PointerBefore;

    /// <summary>Gets the bit mask of the node types the iterator presents.</summary>
    [IdlMember("whatToShow", "https://dom.spec.whatwg.org/#dom-nodeiterator-whattoshow")]
    public uint WhatToShow => _traverser.WhatToShow;

    /// <summary>Gets the filter used to accept, reject or skip nodes, or null when there is none.</summary>
    [IdlMember("filter", "https://dom.spec.whatwg.org/#dom-nodeiterator-filter")]
    public INodeFilter? Filter => _traverser.Filter;

    /// <summary>Moves the iterator to the next accepted node and returns it, or null when there is none.</summary>
    [IdlMember("nextNode", "https://dom.spec.whatwg.org/#dom-nodeiterator-nextnode")]
    public Node? NextNode() => Traverse(next: true);

    /// <summary>Moves the iterator to the previous accepted node and returns it, or null when there is none.</summary>
    [IdlMember("previousNode", "https://dom.spec.whatwg.org/#dom-nodeiterator-previousnode")]
    public Node? PreviousNode() => Traverse(next: false);

    /// <summary>Does nothing (https://dom.spec.whatwg.org/#dom-nodeiterator-detach).</summary>
    [IdlMember("detach", "https://dom.spec.whatwg.org/#dom-nodeiterator-detach")]
    public void Detach()
    {
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-nodeiterator-traverse</summary>
    private Node? Traverse(bool next)
    {
        _candidateReference = _reference;
        Node? result = null;
        while (true)
        {
            var candidate = _candidateReference.Value;
            if (next)
            {
                if (!candidate.PointerBefore)
                {
                    var following = candidate.Node.GetNextInTreeOrder(Root);
                    if (following is null)
                        break;

                    _candidateReference = (following, false);
                }
                else
                {
                    _candidateReference = (candidate.Node, false);
                }
            }
            else
            {
                if (candidate.PointerBefore)
                {
                    var preceding = candidate.Node.GetPreviousInTreeOrder(Root);
                    if (preceding is null)
                        break;

                    _candidateReference = (preceding, true);
                }
                else
                {
                    _candidateReference = (candidate.Node, true);
                }
            }

            var node = _candidateReference.Value.Node;
            ushort filterResult;
            try
            {
                filterResult = _traverser.FilterNode(node);
            }
            catch
            {
                _candidateReference = null;
                throw;
            }

            if (filterResult == NodeFilter.FilterAccept)
            {
                _reference = _candidateReference.Value;
                result = node;
                break;
            }
        }

        _candidateReference = null;
        return result;
    }

    /// <summary>https://dom.spec.whatwg.org/#nodeiterator-pre-removing-steps</summary>
    internal void PreRemovingSteps(Node toBeRemovedNode)
    {
        _reference = AdjustNodePointer(_reference, toBeRemovedNode);
        if (_candidateReference is { } candidate)
        {
            _candidateReference = AdjustNodePointer(candidate, toBeRemovedNode);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#adjust-a-node-pointer</summary>
    private (Node Node, bool PointerBefore) AdjustNodePointer((Node Node, bool PointerBefore) nodePointer, Node toBeRemovedNode)
    {
        if (!toBeRemovedNode.IsInclusiveAncestorOf(nodePointer.Node) || toBeRemovedNode.IsInclusiveAncestorOf(Root))
            return nodePointer;

        if (nodePointer.PointerBefore)
        {
            var next = toBeRemovedNode.GetNextInTreeOrderSkippingChildren(Root);
            if (next is not null)
                return (next, true);
        }

        Node newNode;
        if (toBeRemovedNode.PreviousSibling is null)
        {
            newNode = toBeRemovedNode.ParentNode!;
        }
        else
        {
            newNode = toBeRemovedNode.PreviousSibling;
            while (newNode.LastChild is not null)
            {
                newNode = newNode.LastChild;
            }
        }

        return (newNode, false);
    }
}
