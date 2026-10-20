using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-abstractrange</summary>
/// <seealso href="https://dom.spec.whatwg.org/#abstractrange"/>
[IdlInterface("AbstractRange", "https://dom.spec.whatwg.org/#abstractrange")]
public abstract class AbstractRange
{
    private protected AbstractRange(Node startContainer, uint startOffset, Node endContainer, uint endOffset)
    {
        StartContainerInternal = startContainer;
        StartOffsetInternal = startOffset;
        EndContainerInternal = endContainer;
        EndOffsetInternal = endOffset;
    }

    private protected Node StartContainerInternal { get; set; }

    private protected uint StartOffsetInternal { get; set; }

    private protected Node EndContainerInternal { get; set; }

    private protected uint EndOffsetInternal { get; set; }

    /// <summary>Gets the node in which the range starts.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-startcontainer"/>
    [IdlMember("startContainer", "https://dom.spec.whatwg.org/#dom-range-startcontainer")]
    public Node StartContainer => StartContainerInternal;

    /// <summary>Gets the offset of the start of the range within <c>startContainer</c>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-startoffset"/>
    [IdlMember("startOffset", "https://dom.spec.whatwg.org/#dom-range-startoffset")]
    public uint StartOffset => StartOffsetInternal;

    /// <summary>Gets the node in which the range ends.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-endcontainer"/>
    [IdlMember("endContainer", "https://dom.spec.whatwg.org/#dom-range-endcontainer")]
    public Node EndContainer => EndContainerInternal;

    /// <summary>Gets the offset of the end of the range within <c>endContainer</c>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-endoffset"/>
    [IdlMember("endOffset", "https://dom.spec.whatwg.org/#dom-range-endoffset")]
    public uint EndOffset => EndOffsetInternal;

    /// <summary>Gets a value indicating whether the start and the end of the range are the same point.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-collapsed"/>
    [IdlMember("collapsed", "https://dom.spec.whatwg.org/#dom-range-collapsed")]
    public bool Collapsed => ReferenceEquals(StartContainerInternal, EndContainerInternal) && StartOffsetInternal == EndOffsetInternal;

    /// <summary>https://dom.spec.whatwg.org/#concept-node-length</summary>
    internal static uint GetNodeLength(Node node) => node switch
    {
        DocumentType => 0,
        CharacterData characterData => characterData.Length,
        _ => (uint)node.ChildCount,
    };

    internal enum BoundaryPointPosition
    {
        Before,
        Equal,
        After,
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-range-bp-position</summary>
    internal static BoundaryPointPosition ComparePoints(Node nodeA, uint offsetA, Node nodeB, uint offsetB)
    {
        if (ReferenceEquals(nodeA, nodeB))
        {
            return offsetA == offsetB ? BoundaryPointPosition.Equal : offsetA < offsetB ? BoundaryPointPosition.Before : BoundaryPointPosition.After;
        }

        if (nodeB.IsPreceding(nodeA))
        {
            var position = ComparePoints(nodeB, offsetB, nodeA, offsetA);
            return position switch
            {
                BoundaryPointPosition.Before => BoundaryPointPosition.After,
                BoundaryPointPosition.After => BoundaryPointPosition.Before,
                _ => position,
            };
        }

        if (nodeA.IsAncestorOf(nodeB))
        {
            var child = nodeB;
            while (!ReferenceEquals(child.ParentNode, nodeA))
            {
                child = child.ParentNode!;
            }

            if (child.GetIndex() < offsetA)
                return BoundaryPointPosition.After;
        }

        return BoundaryPointPosition.Before;
    }

    /// <summary>
    /// The position of a boundary point relative to another one of the same document in shadow-including tree order
    /// (https://dom.spec.whatwg.org/#concept-shadow-including-tree-order), for the rendering of the ranges whose boundary points
    /// are in different trees: a boundary point in a shadow tree is between the start of its shadow host and the first child of
    /// the host (the shadow root precedes the children of its host).
    /// </summary>
    internal static BoundaryPointPosition CompareComposedPoints(Node nodeA, uint offsetA, Node nodeB, uint offsetB)
    {
        var rootA = nodeA.GetRoot();
        var rootB = nodeB.GetRoot();
        if (ReferenceEquals(rootA, rootB))
            return ComparePoints(nodeA, offsetA, nodeB, offsetB);

        // The trees of the shadow-including inclusive ancestors of the first node, then the nearest one that contains a
        // shadow-including inclusive ancestor of the second node
        var roots = new List<Node> { rootA };
        while (roots[^1] is ShadowRoot shadowRoot)
        {
            roots.Add(shadowRoot.Host.GetRoot());
        }

        var commonRoot = rootB;
        while (!roots.Contains(commonRoot))
        {
            if (commonRoot is not ShadowRoot shadowRoot)
                return BoundaryPointPosition.Before;

            commonRoot = shadowRoot.Host.GetRoot();
        }

        var inShadowTreeA = Rescope(ref nodeA, ref offsetA, commonRoot);
        var inShadowTreeB = Rescope(ref nodeB, ref offsetB, commonRoot);
        var position = ComparePoints(nodeA, offsetA, nodeB, offsetB);
        if (position != BoundaryPointPosition.Equal || inShadowTreeA == inShadowTreeB)
            return position;

        return inShadowTreeA ? BoundaryPointPosition.Before : BoundaryPointPosition.After;

        // Moves a boundary point of a shadow tree to the start of the shadow-including ancestor host that is in the tree
        static bool Rescope(ref Node node, ref uint offset, Node root)
        {
            var rescoped = false;
            while (node.GetRoot() is var nodeRoot && !ReferenceEquals(nodeRoot, root))
            {
                node = ((ShadowRoot)nodeRoot).Host;
                offset = 0;
                rescoped = true;
            }

            return rescoped;
        }
    }
}

/// <summary>Represents the boundary points used to create a <c>StaticRange</c>.</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-staticrangeinit"/>
[IdlInterface("StaticRangeInit", "https://dom.spec.whatwg.org/#dictdef-staticrangeinit")]
public sealed class StaticRangeInit
{
    /// <summary>Gets or sets the node in which the range starts.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-staticrangeinit-startcontainer"/>
    [IdlMember("startContainer", "https://dom.spec.whatwg.org/#dom-staticrangeinit-startcontainer")]
    public required Node StartContainer { get; init; }

    /// <summary>Gets or sets the offset of the start of the range within the start container.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-staticrangeinit-startoffset"/>
    [IdlMember("startOffset", "https://dom.spec.whatwg.org/#dom-staticrangeinit-startoffset")]
    public required uint StartOffset { get; init; }

    /// <summary>Gets or sets the node in which the range ends.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-staticrangeinit-endcontainer"/>
    [IdlMember("endContainer", "https://dom.spec.whatwg.org/#dom-staticrangeinit-endcontainer")]
    public required Node EndContainer { get; init; }

    /// <summary>Gets or sets the offset of the end of the range within the end container.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-staticrangeinit-endoffset"/>
    [IdlMember("endOffset", "https://dom.spec.whatwg.org/#dom-staticrangeinit-endoffset")]
    public required uint EndOffset { get; init; }
}

/// <summary>https://dom.spec.whatwg.org/#interface-staticrange</summary>
/// <seealso href="https://dom.spec.whatwg.org/#staticrange"/>
[IdlInterface("StaticRange", "https://dom.spec.whatwg.org/#staticrange")]
public sealed class StaticRange : AbstractRange
{
    /// <summary>Creates a static range with the boundary points of the given initializer.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-staticrange-staticrange"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-staticrange-staticrange")]
    public StaticRange(StaticRangeInit init)
        : base(Validate(init).StartContainer, init.StartOffset, init.EndContainer, init.EndOffset)
    {
    }

    /// <summary>
    /// Whether the static range is valid (https://dom.spec.whatwg.org/#staticrange-valid): its offsets are not greater than the
    /// lengths of their nodes, its nodes are in the same tree, and its start is not after its end. The boundary points of a
    /// static range are not updated by the mutations of the tree.
    /// </summary>
    internal bool IsValid => StartOffset <= GetNodeLength(StartContainer) && EndOffset <= GetNodeLength(EndContainer)
        && ReferenceEquals(StartContainer.GetRoot(), EndContainer.GetRoot())
        && Range.ComparePoints(StartContainer, StartOffset, EndContainer, EndOffset) != BoundaryPointPosition.After;

    private static StaticRangeInit Validate(StaticRangeInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        if (init.StartContainer is DocumentType or Attr || init.EndContainer is DocumentType or Attr)
            throw new DomException("The container cannot be a doctype or an attribute", "InvalidNodeTypeError");

        return init;
    }
}

/// <summary>https://dom.spec.whatwg.org/#interface-range</summary>
/// <seealso href="https://dom.spec.whatwg.org/#range"/>
[IdlInterface("Range", "https://dom.spec.whatwg.org/#range")]
public sealed partial class Range : AbstractRange
{
    /// <summary>Compares the start of the given range to the start of this range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-start_to_start"/>
    [IdlMember("START_TO_START", "https://dom.spec.whatwg.org/#dom-range-start_to_start")]
    public const ushort StartToStart = 0;

    /// <summary>Compares the end of the given range to the start of this range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-start_to_end"/>
    [IdlMember("START_TO_END", "https://dom.spec.whatwg.org/#dom-range-start_to_end")]
    public const ushort StartToEnd = 1;

    /// <summary>Compares the end of the given range to the end of this range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-end_to_end"/>
    [IdlMember("END_TO_END", "https://dom.spec.whatwg.org/#dom-range-end_to_end")]
    public const ushort EndToEnd = 2;

    /// <summary>Compares the start of the given range to the end of this range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-end_to_start"/>
    [IdlMember("END_TO_START", "https://dom.spec.whatwg.org/#dom-range-end_to_start")]
    public const ushort EndToStart = 3;

    private Document _registeredDocument;

    /// <summary>The selection whose range is this range (https://w3c.github.io/selection-api/#dfn-range), if any.</summary>
    internal Selection? Selection { get; set; }

    /// <summary>Creates a collapsed range at the start of the current global document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-range"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-range-range")]
    public Range()
        : this(Document.CurrentGlobalDocument)
    {
    }

    internal Range(Document document)
        : this(document, 0, document, 0)
    {
    }

    internal Range(Node startContainer, uint startOffset, Node endContainer, uint endOffset)
        : base(startContainer, startOffset, endContainer, endOffset)
    {
        _registeredDocument = startContainer.NodeDocument;
        _registeredDocument.RegisterLiveRange(this);
    }

    /// <summary>Live ranges are registered with the document of their boundary points so the mutation algorithms can update them.</summary>
    private void UpdateRegistration()
    {
        var document = StartContainerInternal.NodeDocument;
        if (!ReferenceEquals(document, _registeredDocument))
        {
            _registeredDocument.UnregisterLiveRange(this);
            _registeredDocument = document;
            document.RegisterLiveRange(this);
        }
    }

    private void SetStartCore(Node node, uint offset)
    {
        StartContainerInternal = node;
        StartOffsetInternal = offset;
        UpdateRegistration();
        Selection?.OnChanged();
    }

    private void SetEndCore(Node node, uint offset)
    {
        EndContainerInternal = node;
        EndOffsetInternal = offset;
        Selection?.OnChanged();
    }

    private Node Root => StartContainerInternal.GetRoot();

    /// <summary>https://dom.spec.whatwg.org/#dom-range-commonancestorcontainer</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-commonancestorcontainer"/>
    [IdlMember("commonAncestorContainer", "https://dom.spec.whatwg.org/#dom-range-commonancestorcontainer")]
    public Node CommonAncestorContainer
    {
        get
        {
            var container = StartContainerInternal;
            while (!container.IsInclusiveAncestorOf(EndContainerInternal))
            {
                container = container.ParentNode!;
            }

            return container;
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-range-bp-set</summary>
    private void SetBoundary(Node node, uint offset, bool isStart)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DocumentType)
            throw new DomException("The node cannot be a doctype", "InvalidNodeTypeError");

        if (offset > GetNodeLength(node))
            throw DomException.IndexSize("The offset is greater than the length of the node");

        if (isStart)
        {
            if (!ReferenceEquals(Root, node.GetRoot()) || ComparePoints(node, offset, EndContainerInternal, EndOffsetInternal) == BoundaryPointPosition.After)
            {
                SetEndCore(node, offset);
            }

            SetStartCore(node, offset);
        }
        else
        {
            if (!ReferenceEquals(Root, node.GetRoot()) || ComparePoints(node, offset, StartContainerInternal, StartOffsetInternal) == BoundaryPointPosition.Before)
            {
                SetStartCore(node, offset);
            }

            SetEndCore(node, offset);
        }
    }

    /// <summary>Sets the start of the range to the given node and offset.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-setstart"/>
    [IdlMember("setStart", "https://dom.spec.whatwg.org/#dom-range-setstart")]
    public void SetStart(Node node, uint offset) => SetBoundary(node, offset, isStart: true);

    /// <summary>Sets the end of the range to the given node and offset.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-setend"/>
    [IdlMember("setEnd", "https://dom.spec.whatwg.org/#dom-range-setend")]
    public void SetEnd(Node node, uint offset) => SetBoundary(node, offset, isStart: false);

    /// <summary>Sets the start of the range to the position just before the given node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-setstartbefore"/>
    [IdlMember("setStartBefore", "https://dom.spec.whatwg.org/#dom-range-setstartbefore")]
    public void SetStartBefore(Node node) => SetBoundary(GetParentOrThrow(node), (uint)node.GetIndex(), isStart: true);

    /// <summary>Sets the start of the range to the position just after the given node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-setstartafter"/>
    [IdlMember("setStartAfter", "https://dom.spec.whatwg.org/#dom-range-setstartafter")]
    public void SetStartAfter(Node node) => SetBoundary(GetParentOrThrow(node), (uint)node.GetIndex() + 1, isStart: true);

    /// <summary>Sets the end of the range to the position just before the given node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-setendbefore"/>
    [IdlMember("setEndBefore", "https://dom.spec.whatwg.org/#dom-range-setendbefore")]
    public void SetEndBefore(Node node) => SetBoundary(GetParentOrThrow(node), (uint)node.GetIndex(), isStart: false);

    /// <summary>Sets the end of the range to the position just after the given node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-setendafter"/>
    [IdlMember("setEndAfter", "https://dom.spec.whatwg.org/#dom-range-setendafter")]
    public void SetEndAfter(Node node) => SetBoundary(GetParentOrThrow(node), (uint)node.GetIndex() + 1, isStart: false);

    private static Node GetParentOrThrow(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.ParentNode ?? throw new DomException("The node has no parent", "InvalidNodeTypeError");
    }

    /// <summary>Collapses the range to its start when <c>toStart</c> is true, or to its end otherwise.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-collapse"/>
    [IdlMember("collapse", "https://dom.spec.whatwg.org/#dom-range-collapse")]
    public void Collapse(bool toStart = false)
    {
        if (toStart)
        {
            SetEndCore(StartContainerInternal, StartOffsetInternal);
        }
        else
        {
            SetStartCore(EndContainerInternal, EndOffsetInternal);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-range-select</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-selectnode"/>
    [IdlMember("selectNode", "https://dom.spec.whatwg.org/#dom-range-selectnode")]
    public void SelectNode(Node node)
    {
        var parent = GetParentOrThrow(node);
        var index = (uint)node.GetIndex();
        SetStartCore(parent, index);
        SetEndCore(parent, index + 1);
    }

    /// <summary>Sets the range to contain all the children of the given node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-selectnodecontents"/>
    [IdlMember("selectNodeContents", "https://dom.spec.whatwg.org/#dom-range-selectnodecontents")]
    public void SelectNodeContents(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DocumentType)
            throw new DomException("The node cannot be a doctype", "InvalidNodeTypeError");

        var length = GetNodeLength(node);
        SetStartCore(node, 0);
        SetEndCore(node, length);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-range-compareboundarypoints</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-compareboundarypoints"/>
    [IdlMember("compareBoundaryPoints", "https://dom.spec.whatwg.org/#dom-range-compareboundarypoints")]
    public short CompareBoundaryPoints(ushort how, Range sourceRange)
    {
        ArgumentNullException.ThrowIfNull(sourceRange);
        if (how > EndToStart)
            throw DomException.NotSupported("Invalid comparison type");

        if (!ReferenceEquals(Root, sourceRange.Root))
            throw new DomException("The ranges are not in the same tree", "WrongDocumentError");

        var (thisNode, thisOffset, sourceNode, sourceOffset) = how switch
        {
            StartToStart => (StartContainerInternal, StartOffsetInternal, sourceRange.StartContainerInternal, sourceRange.StartOffsetInternal),
            StartToEnd => (EndContainerInternal, EndOffsetInternal, sourceRange.StartContainerInternal, sourceRange.StartOffsetInternal),
            EndToEnd => (EndContainerInternal, EndOffsetInternal, sourceRange.EndContainerInternal, sourceRange.EndOffsetInternal),
            _ => (StartContainerInternal, StartOffsetInternal, sourceRange.EndContainerInternal, sourceRange.EndOffsetInternal),
        };

        return ComparePoints(thisNode, thisOffset, sourceNode, sourceOffset) switch
        {
            BoundaryPointPosition.Before => -1,
            BoundaryPointPosition.Equal => 0,
            _ => 1,
        };
    }

    /// <summary>https://dom.spec.whatwg.org/#contained</summary>
    private bool IsContained(Node node) =>
        ReferenceEquals(node.GetRoot(), Root) &&
        ComparePoints(node, 0, StartContainerInternal, StartOffsetInternal) == BoundaryPointPosition.After &&
        ComparePoints(node, GetNodeLength(node), EndContainerInternal, EndOffsetInternal) == BoundaryPointPosition.Before;

    /// <summary>https://dom.spec.whatwg.org/#partially-contained</summary>
    private bool IsPartiallyContained(Node node) =>
        node.IsInclusiveAncestorOf(StartContainerInternal) != node.IsInclusiveAncestorOf(EndContainerInternal);

    /// <summary>https://dom.spec.whatwg.org/#dom-range-deletecontents</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-deletecontents"/>
    [IdlMember("deleteContents", "https://dom.spec.whatwg.org/#dom-range-deletecontents")]
    public void DeleteContents()
    {
        using var ceReactions = Html.CEReactionsScope.Enter();
        if (Collapsed)
            return;

        var originalStartNode = StartContainerInternal;
        var originalStartOffset = StartOffsetInternal;
        var originalEndNode = EndContainerInternal;
        var originalEndOffset = EndOffsetInternal;
        if (ReferenceEquals(originalStartNode, originalEndNode) && originalStartNode is CharacterData characterData)
        {
            characterData.ReplaceData(originalStartOffset, originalEndOffset - originalStartOffset, "");
            return;
        }

        var nodesToRemove = new List<Node>();
        var root = CommonAncestorContainer;
        for (var node = root.FirstChild; node is not null;)
        {
            if (IsContained(node))
            {
                nodesToRemove.Add(node);
                node = node.GetNextInTreeOrderSkippingChildren(root);
            }
            else
            {
                node = node.GetNextInTreeOrder(root);
            }
        }

        var (newNode, newOffset) = ComputeNewPosition(originalStartNode, originalStartOffset, originalEndNode);
        SetStartCore(newNode, newOffset);
        SetEndCore(newNode, newOffset);

        if (originalStartNode is CharacterData startData)
        {
            startData.ReplaceData(originalStartOffset, startData.Length - originalStartOffset, "");
        }

        foreach (var node in nodesToRemove)
        {
            node.RemoveFromParent();
        }

        if (originalEndNode is CharacterData endData)
        {
            endData.ReplaceData(0, originalEndOffset, "");
        }
    }

    private static (Node Node, uint Offset) ComputeNewPosition(Node originalStartNode, uint originalStartOffset, Node originalEndNode)
    {
        if (originalStartNode.IsInclusiveAncestorOf(originalEndNode))
            return (originalStartNode, originalStartOffset);

        var referenceNode = originalStartNode;
        while (referenceNode.ParentNode is not null && !referenceNode.ParentNode.IsInclusiveAncestorOf(originalEndNode))
        {
            referenceNode = referenceNode.ParentNode;
        }

        return (referenceNode.ParentNode!, (uint)referenceNode.GetIndex() + 1);
    }

    /// <summary>Removes the contents of the range from the tree and returns them in a document fragment.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-extractcontents"/>
    [IdlMember("extractContents", "https://dom.spec.whatwg.org/#dom-range-extractcontents")]
    public DocumentFragment ExtractContents()
    {
        using var ceReactions = Html.CEReactionsScope.Enter();
        return Extract();
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-range-extract</summary>
    private DocumentFragment Extract()
    {
        var fragment = new DocumentFragment(StartContainerInternal.NodeDocument);
        if (Collapsed)
            return fragment;

        var originalStartNode = StartContainerInternal;
        var originalStartOffset = StartOffsetInternal;
        var originalEndNode = EndContainerInternal;
        var originalEndOffset = EndOffsetInternal;
        if (ReferenceEquals(originalStartNode, originalEndNode) && originalStartNode is CharacterData characterData)
        {
            var clone = (CharacterData)characterData.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            clone.SetDataCore(characterData.SubstringData(originalStartOffset, originalEndOffset - originalStartOffset));
            fragment.AppendChildCore(clone);
            characterData.ReplaceData(originalStartOffset, originalEndOffset - originalStartOffset, "");
            return fragment;
        }

        var (commonAncestor, firstPartiallyContainedChild, lastPartiallyContainedChild, containedChildren) = ComputeContainment(originalStartNode, originalEndNode);
        var (newNode, newOffset) = ComputeNewPosition(originalStartNode, originalStartOffset, originalEndNode);
        _ = commonAncestor;
        SetStartCore(newNode, newOffset);
        SetEndCore(newNode, newOffset);

        if (firstPartiallyContainedChild is CharacterData firstData)
        {
            var clone = (CharacterData)firstData.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            clone.SetDataCore(firstData.SubstringData(originalStartOffset, firstData.Length - originalStartOffset));
            fragment.AppendChildCore(clone);
            firstData.ReplaceData(originalStartOffset, firstData.Length - originalStartOffset, "");
        }
        else if (firstPartiallyContainedChild is not null)
        {
            var clone = firstPartiallyContainedChild.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            fragment.AppendChildCore(clone);
            var subrange = new Range(originalStartNode, originalStartOffset, firstPartiallyContainedChild, GetNodeLength(firstPartiallyContainedChild));
            clone.AppendChildCore(subrange.Extract());
            subrange.Detach();
        }

        foreach (var containedChild in containedChildren)
        {
            fragment.AppendChildCore(containedChild);
        }

        if (lastPartiallyContainedChild is CharacterData lastData)
        {
            var clone = (CharacterData)lastData.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            clone.SetDataCore(lastData.SubstringData(0, originalEndOffset));
            fragment.AppendChildCore(clone);
            lastData.ReplaceData(0, originalEndOffset, "");
        }
        else if (lastPartiallyContainedChild is not null)
        {
            var clone = lastPartiallyContainedChild.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            fragment.AppendChildCore(clone);
            var subrange = new Range(lastPartiallyContainedChild, 0, originalEndNode, originalEndOffset);
            clone.AppendChildCore(subrange.Extract());
            subrange.Detach();
        }

        return fragment;
    }

    private (Node CommonAncestor, Node? FirstPartiallyContainedChild, Node? LastPartiallyContainedChild, List<Node> ContainedChildren) ComputeContainment(Node originalStartNode, Node originalEndNode)
    {
        var commonAncestor = CommonAncestorContainer;
        Node? firstPartiallyContainedChild = null;
        if (!originalStartNode.IsInclusiveAncestorOf(originalEndNode))
        {
            for (var child = commonAncestor.FirstChild; child is not null; child = child.NextSibling)
            {
                if (IsPartiallyContained(child))
                {
                    firstPartiallyContainedChild = child;
                    break;
                }
            }
        }

        Node? lastPartiallyContainedChild = null;
        if (!originalEndNode.IsInclusiveAncestorOf(originalStartNode))
        {
            for (var child = commonAncestor.LastChild; child is not null; child = child.PreviousSibling)
            {
                if (IsPartiallyContained(child))
                {
                    lastPartiallyContainedChild = child;
                    break;
                }
            }
        }

        var containedChildren = new List<Node>();
        for (var child = commonAncestor.FirstChild; child is not null; child = child.NextSibling)
        {
            if (IsContained(child))
            {
                containedChildren.Add(child);
            }
        }

        foreach (var child in containedChildren)
        {
            if (child is DocumentType)
                throw DomException.HierarchyRequest("A doctype cannot be extracted");
        }

        return (commonAncestor, firstPartiallyContainedChild, lastPartiallyContainedChild, containedChildren);
    }

    /// <summary>Returns a document fragment containing a copy of the contents of the range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-clonecontents"/>
    [IdlMember("cloneContents", "https://dom.spec.whatwg.org/#dom-range-clonecontents")]
    public DocumentFragment CloneContents()
    {
        using var ceReactions = Html.CEReactionsScope.Enter();
        return CloneTheContents();
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-range-clone</summary>
    private DocumentFragment CloneTheContents()
    {
        var fragment = new DocumentFragment(StartContainerInternal.NodeDocument);
        if (Collapsed)
            return fragment;

        var originalStartNode = StartContainerInternal;
        var originalStartOffset = StartOffsetInternal;
        var originalEndNode = EndContainerInternal;
        var originalEndOffset = EndOffsetInternal;
        if (ReferenceEquals(originalStartNode, originalEndNode) && originalStartNode is CharacterData characterData)
        {
            var clone = (CharacterData)characterData.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            clone.SetDataCore(characterData.SubstringData(originalStartOffset, originalEndOffset - originalStartOffset));
            fragment.AppendChildCore(clone);
            return fragment;
        }

        var (_, firstPartiallyContainedChild, lastPartiallyContainedChild, containedChildren) = ComputeContainment(originalStartNode, originalEndNode);
        if (firstPartiallyContainedChild is CharacterData firstData)
        {
            var clone = (CharacterData)firstData.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            clone.SetDataCore(firstData.SubstringData(originalStartOffset, firstData.Length - originalStartOffset));
            fragment.AppendChildCore(clone);
        }
        else if (firstPartiallyContainedChild is not null)
        {
            var clone = firstPartiallyContainedChild.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            fragment.AppendChildCore(clone);
            var subrange = new Range(originalStartNode, originalStartOffset, firstPartiallyContainedChild, GetNodeLength(firstPartiallyContainedChild));
            clone.AppendChildCore(subrange.CloneTheContents());
            subrange.Detach();
        }

        foreach (var containedChild in containedChildren)
        {
            fragment.AppendChildCore(containedChild.Clone(document: null, subtree: true, parent: null, fallbackRegistry: null));
        }

        if (lastPartiallyContainedChild is CharacterData lastData)
        {
            var clone = (CharacterData)lastData.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            clone.SetDataCore(lastData.SubstringData(0, originalEndOffset));
            fragment.AppendChildCore(clone);
        }
        else if (lastPartiallyContainedChild is not null)
        {
            var clone = lastPartiallyContainedChild.Clone(document: null, subtree: false, parent: null, fallbackRegistry: null);
            fragment.AppendChildCore(clone);
            var subrange = new Range(lastPartiallyContainedChild, 0, originalEndNode, originalEndOffset);
            clone.AppendChildCore(subrange.CloneTheContents());
            subrange.Detach();
        }

        return fragment;
    }

    /// <summary>Inserts the given node into the tree at the start of the range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-insertnode"/>
    [IdlMember("insertNode", "https://dom.spec.whatwg.org/#dom-range-insertnode")]
    public void InsertNode(Node node)
    {
        using var ceReactions = Html.CEReactionsScope.Enter();
        ArgumentNullException.ThrowIfNull(node);
        InsertNodeCore(node);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-range-insert</summary>
    private void InsertNodeCore(Node node)
    {
        var startNode = StartContainerInternal;
        if (startNode is ProcessingInstruction or Comment || (startNode is Text && startNode.ParentNode is null) || ReferenceEquals(startNode, node))
            throw DomException.HierarchyRequest("Cannot insert a node at this position");

        Node? referenceNode;
        if (startNode is Text)
        {
            referenceNode = startNode;
        }
        else
        {
            referenceNode = startNode.FirstChild;
            for (var i = 0u; i < StartOffsetInternal && referenceNode is not null; i++)
            {
                referenceNode = referenceNode.NextSibling;
            }
        }

        var parent = referenceNode is null ? startNode : referenceNode.ParentNode!;
        parent.EnsurePreInsertValidity(node, referenceNode);
        if (startNode is Text text)
        {
            referenceNode = text.SplitText(StartOffsetInternal);
        }

        if (ReferenceEquals(node, referenceNode))
        {
            referenceNode = referenceNode.NextSibling;
        }

        node.RemoveFromParent();
        var newOffset = referenceNode is null ? GetNodeLength(parent) : (uint)referenceNode.GetIndex();
        newOffset += node is DocumentFragment ? GetNodeLength(node) : 1;
        parent.PreInsert(node, referenceNode);
        if (Collapsed)
        {
            SetEndCore(parent, newOffset);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-range-surroundcontents</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-surroundcontents"/>
    [IdlMember("surroundContents", "https://dom.spec.whatwg.org/#dom-range-surroundcontents")]
    public void SurroundContents(Node newParent)
    {
        using var ceReactions = Html.CEReactionsScope.Enter();
        ArgumentNullException.ThrowIfNull(newParent);
        var root = CommonAncestorContainer;
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is not Text && IsPartiallyContained(node))
                throw DomException.InvalidState("A non-Text node is partially contained in the range");
        }

        if (newParent is Document or DocumentType or DocumentFragment)
            throw new DomException("Invalid new parent", "InvalidNodeTypeError");

        var fragment = Extract();
        if (newParent.HasChildNodes())
        {
            newParent.ReplaceAll(null);
        }

        InsertNodeCore(newParent);
        newParent.AppendNode(fragment);
        SelectNode(newParent);
    }

    /// <summary>Returns a new range with the same boundary points as this range.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-clonerange"/>
    [IdlMember("cloneRange", "https://dom.spec.whatwg.org/#dom-range-clonerange")]
    public Range CloneRange() => new(StartContainerInternal, StartOffsetInternal, EndContainerInternal, EndOffsetInternal);

    /// <summary>Does nothing (https://dom.spec.whatwg.org/#dom-range-detach).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-detach"/>
    [IdlMember("detach", "https://dom.spec.whatwg.org/#dom-range-detach")]
    public void Detach()
    {
        // The method does nothing per the specification. Internally it stops tracking temporary ranges.
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-range-ispointinrange</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-ispointinrange"/>
    [IdlMember("isPointInRange", "https://dom.spec.whatwg.org/#dom-range-ispointinrange")]
    public bool IsPointInRange(Node node, uint offset)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(node.GetRoot(), Root))
            return false;

        if (node is DocumentType)
            throw new DomException("The node cannot be a doctype", "InvalidNodeTypeError");

        if (offset > GetNodeLength(node))
            throw DomException.IndexSize("The offset is greater than the length of the node");

        return ComparePoints(node, offset, StartContainerInternal, StartOffsetInternal) != BoundaryPointPosition.Before &&
            ComparePoints(node, offset, EndContainerInternal, EndOffsetInternal) != BoundaryPointPosition.After;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-range-comparepoint</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-comparepoint"/>
    [IdlMember("comparePoint", "https://dom.spec.whatwg.org/#dom-range-comparepoint")]
    public short ComparePoint(Node node, uint offset)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(node.GetRoot(), Root))
            throw new DomException("The node is not in the same tree as the range", "WrongDocumentError");

        if (node is DocumentType)
            throw new DomException("The node cannot be a doctype", "InvalidNodeTypeError");

        if (offset > GetNodeLength(node))
            throw DomException.IndexSize("The offset is greater than the length of the node");

        if (ComparePoints(node, offset, StartContainerInternal, StartOffsetInternal) == BoundaryPointPosition.Before)
            return -1;

        if (ComparePoints(node, offset, EndContainerInternal, EndOffsetInternal) == BoundaryPointPosition.After)
            return 1;

        return 0;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-range-intersectsnode</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-range-intersectsnode"/>
    [IdlMember("intersectsNode", "https://dom.spec.whatwg.org/#dom-range-intersectsnode")]
    public bool IntersectsNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(node.GetRoot(), Root))
            return false;

        var parent = node.ParentNode;
        if (parent is null)
            return true;

        var offset = (uint)node.GetIndex();
        return ComparePoints(parent, offset, EndContainerInternal, EndOffsetInternal) == BoundaryPointPosition.Before &&
            ComparePoints(parent, offset + 1, StartContainerInternal, StartOffsetInternal) == BoundaryPointPosition.After;
    }

    /// <summary>The stringification behavior (https://dom.spec.whatwg.org/#dom-range-stringifier).</summary>
    public override string ToString()
    {
        if (ReferenceEquals(StartContainerInternal, EndContainerInternal) && StartContainerInternal is Text text)
            return text.Data.Substring((int)StartOffsetInternal, (int)(EndOffsetInternal - StartOffsetInternal));

        var builder = new StringBuilder();
        if (StartContainerInternal is Text startText)
        {
            builder.Append(startText.Data.AsSpan((int)StartOffsetInternal));
        }

        var root = CommonAncestorContainer;
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Text containedText && IsContained(node))
            {
                builder.Append(containedText.Data);
            }
        }

        if (EndContainerInternal is Text endText)
        {
            builder.Append(endText.Data.AsSpan(0, (int)EndOffsetInternal));
        }

        return builder.ToString();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Live range updates
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The live range steps of the insert algorithm.</summary>
    internal void OnInsertion(Node parent, int childIndex, int count)
    {
        if (ReferenceEquals(StartContainerInternal, parent) && StartOffsetInternal > childIndex)
        {
            StartOffsetInternal += (uint)count;
        }

        if (ReferenceEquals(EndContainerInternal, parent) && EndOffsetInternal > childIndex)
        {
            EndOffsetInternal += (uint)count;
        }

        Selection?.OnChanged();
    }

    /// <summary>https://dom.spec.whatwg.org/#live-range-pre-remove-steps</summary>
    internal void OnPreRemove(Node node, Node parent, int index)
    {
        if (node.IsInclusiveAncestorOf(StartContainerInternal))
        {
            StartContainerInternal = parent;
            StartOffsetInternal = (uint)index;
        }

        if (node.IsInclusiveAncestorOf(EndContainerInternal))
        {
            EndContainerInternal = parent;
            EndOffsetInternal = (uint)index;
        }

        if (ReferenceEquals(StartContainerInternal, parent) && StartOffsetInternal > index)
        {
            StartOffsetInternal--;
        }

        if (ReferenceEquals(EndContainerInternal, parent) && EndOffsetInternal > index)
        {
            EndOffsetInternal--;
        }

        Selection?.OnChanged();
    }

    /// <summary>The live range steps of the "replace data" algorithm (https://dom.spec.whatwg.org/#concept-cd-replace).</summary>
    internal void OnReplaceData(Node node, uint offset, uint count, uint dataLength)
    {
        if (ReferenceEquals(StartContainerInternal, node) && StartOffsetInternal > offset && StartOffsetInternal <= offset + count)
        {
            StartOffsetInternal = offset;
        }

        if (ReferenceEquals(EndContainerInternal, node) && EndOffsetInternal > offset && EndOffsetInternal <= offset + count)
        {
            EndOffsetInternal = offset;
        }

        if (ReferenceEquals(StartContainerInternal, node) && StartOffsetInternal > offset + count)
        {
            StartOffsetInternal = StartOffsetInternal + dataLength - count;
        }

        if (ReferenceEquals(EndContainerInternal, node) && EndOffsetInternal > offset + count)
        {
            EndOffsetInternal = EndOffsetInternal + dataLength - count;
        }

        Selection?.OnChanged();
    }

    /// <summary>The live range steps of the "split a Text node" algorithm.</summary>
    internal void OnSplitText(Text node, Text newNode, uint offset, Node parent, int nodeIndex)
    {
        if (ReferenceEquals(StartContainerInternal, node) && StartOffsetInternal > offset)
        {
            StartContainerInternal = newNode;
            StartOffsetInternal -= offset;
        }

        if (ReferenceEquals(EndContainerInternal, node) && EndOffsetInternal > offset)
        {
            EndContainerInternal = newNode;
            EndOffsetInternal -= offset;
        }

        if (ReferenceEquals(StartContainerInternal, parent) && StartOffsetInternal == nodeIndex + 1)
        {
            StartOffsetInternal++;
        }

        if (ReferenceEquals(EndContainerInternal, parent) && EndOffsetInternal == nodeIndex + 1)
        {
            EndOffsetInternal++;
        }

        Selection?.OnChanged();
    }

    /// <summary>The live range steps of the normalize algorithm for a merged Text node.</summary>
    internal void OnNormalize(Text node, Text currentNode, Node currentNodeParent, int currentNodeIndex, uint length)
    {
        if (ReferenceEquals(StartContainerInternal, currentNode))
        {
            StartOffsetInternal += length;
            StartContainerInternal = node;
        }

        if (ReferenceEquals(EndContainerInternal, currentNode))
        {
            EndOffsetInternal += length;
            EndContainerInternal = node;
        }

        if (ReferenceEquals(StartContainerInternal, currentNodeParent) && StartOffsetInternal == currentNodeIndex)
        {
            StartContainerInternal = node;
            StartOffsetInternal = length;
        }

        if (ReferenceEquals(EndContainerInternal, currentNodeParent) && EndOffsetInternal == currentNodeIndex)
        {
            EndContainerInternal = node;
            EndOffsetInternal = length;
        }

        Selection?.OnChanged();
    }
}
