using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using DomRange = Meziantou.Framework.WebPlatform.Dom.Range;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The algorithms of the editing specification (https://w3c.github.io/editing/docs/execCommand/), for one call of an
/// editing method of a document. The algorithms edit the DOM at the active range, which is the range of the selection of
/// the document: its boundary points follow the edits, and the node moves of the algorithms preserve them
/// (<see cref="MovePreservingRanges"/>).
/// </summary>
/// <remarks>
/// The definitions that the specification bases on the rendering are computed from the cascade, without layout: a block
/// node is a node whose computed display is not inline-level, and the line breaks that are collapsed or extraneous are
/// found from the contents of their line (see <see cref="IsCollapsedLineBreak"/>).
/// </remarks>
internal sealed partial class EditingAlgorithms
{
    private const ushort DocumentPositionFollowing = 4;

    private readonly Document _document;
    private readonly EditingState _state;
    private readonly StyleEngine _engine;
    private readonly List<DomRange> _extraRanges = [];
    private Dictionary<string, string?>? _normalizedColors;

    /// <summary>The active range (https://w3c.github.io/editing/docs/execCommand/#active-range): the range of the selection, or null.</summary>
    private readonly DomRange? _activeRange;

    public EditingAlgorithms(Document document)
    {
        _document = document;
        _state = document.EditingState;
        _engine = document.GetStyleEngine();

        // The algorithms need the boundary points of the active range to be in Text or Element nodes of the document
        if (document.GetSelection()?.RangeOrNull is { } range
            && range.StartContainer is Text or Element && range.EndContainer is Text or Element
            && document.IsInclusiveAncestorOf(range.StartContainer) && document.IsInclusiveAncestorOf(range.EndContainer))
        {
            _activeRange = range;
        }
    }

    private DomRange ActiveRange => _activeRange!;

    // Tree helpers

    private static Node? NextNode(Node node) => node.FirstChild ?? NextNodeDescendants(node);

    private static Node? PreviousNode(Node node)
    {
        if (node.PreviousSibling is { } previous)
        {
            while (previous.LastChild is { } last)
            {
                previous = last;
            }

            return previous;
        }

        return node.ParentNode as Element;
    }

    private static Node? NextNodeDescendants(Node? node)
    {
        while (node is not null && node.NextSibling is null)
        {
            node = node.ParentNode;
        }

        return node?.NextSibling;
    }

    private static bool IsAncestor(Node? ancestor, Node? descendant)
        => ancestor is not null && descendant is not null && !ReferenceEquals(ancestor, descendant) && ancestor.Contains(descendant);

    private static bool IsDescendant(Node? descendant, Node? ancestor) => IsAncestor(ancestor, descendant);

    /// <summary>Whether the first node is before the second one in tree order (a null second node is the end of the tree).</summary>
    private static bool IsBefore(Node? node1, Node? node2)
        => node1 is not null && (node2 is null || (node1.CompareDocumentPosition(node2) & DocumentPositionFollowing) != 0);

    private static List<Node> GetDescendants(Node node)
    {
        var descendants = new List<Node>();
        var stop = NextNodeDescendants(node);
        for (var current = NextNode(node); current is not null && !ReferenceEquals(current, stop); current = NextNode(current))
        {
            descendants.Add(current);
        }

        return descendants;
    }

    private static bool AnyDescendant(Node node, Func<Node, bool> predicate)
    {
        var stop = NextNodeDescendants(node);
        for (var current = NextNode(node); current is not null && !ReferenceEquals(current, stop); current = NextNode(current))
        {
            if (predicate(current))
                return true;
        }

        return false;
    }

    private static List<Node> GetChildren(Node node)
    {
        var children = new List<Node>(node.ChildCount);
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            children.Add(child);
        }

        return children;
    }

    private static Node? ChildAt(Node node, int index)
    {
        if (index < 0 || index >= node.ChildCount)
            return null;

        var child = node.FirstChild;
        while (index-- > 0)
        {
            child = child!.NextSibling;
        }

        return child;
    }

    private static int NodeIndex(Node node) => node.GetIndex();

    /// <summary>https://dom.spec.whatwg.org/#concept-node-length</summary>
    private static int NodeLength(Node node) => node switch
    {
        DocumentType or ProcessingInstruction => 0,
        CharacterData data => (int)data.Length,
        _ => node.ChildCount,
    };

    /// <summary>
    /// https://dom.spec.whatwg.org/#concept-range-bp-position: the position of the first boundary point relative to the
    /// second one (-1 before, 0 equal, 1 after).
    /// </summary>
    private static int ComparePoints(Node nodeA, int offsetA, Node nodeB, int offsetB)
    {
        if (ReferenceEquals(nodeA, nodeB))
            return offsetA.CompareTo(offsetB);

        if (IsBefore(nodeB, nodeA))
            return -ComparePoints(nodeB, offsetB, nodeA, offsetA);

        if (IsAncestor(nodeA, nodeB))
        {
            var child = nodeB;
            while (!ReferenceEquals(child.ParentNode, nodeA))
            {
                child = child.ParentNode!;
            }

            if (NodeIndex(child) < offsetA)
                return 1;
        }

        return -1;
    }

    private static Node GetRoot(Node node)
    {
        while (node.ParentNode is { } parent)
        {
            node = parent;
        }

        return node;
    }

    /// <summary>https://dom.spec.whatwg.org/#contained</summary>
    private static bool IsContained(Node node, DomRange range)
        => ReferenceEquals(GetRoot(node), GetRoot(range.StartContainer))
            && ComparePoints(node, 0, range.StartContainer, (int)range.StartOffset) > 0
            && ComparePoints(node, NodeLength(node), range.EndContainer, (int)range.EndOffset) < 0;

    /// <summary>
    /// The nodes contained in a range that match a condition, in tree order. Unless <paramref name="all"/> is set, the
    /// descendants of the nodes that are returned are omitted.
    /// </summary>
    private static List<Node> GetContainedNodes(DomRange range, Func<Node, bool>? condition = null, bool all = false)
    {
        var node = range.StartContainer;
        var startOffset = (int)range.StartOffset;
        Node? current;
        if (node.HasChildNodes() && startOffset < node.ChildCount)
        {
            current = ChildAt(node, startOffset);
        }
        else if (startOffset == NodeLength(node))
        {
            current = NextNodeDescendants(node);
        }
        else
        {
            current = NextNode(node);
        }

        var stop = range.EndContainer;
        var endOffset = (int)range.EndOffset;
        var stopNode = stop.HasChildNodes() && endOffset < stop.ChildCount ? ChildAt(stop, endOffset) : NextNodeDescendants(stop);
        var nodes = new List<Node>();
        while (current is not null && IsBefore(current, stopNode))
        {
            if (IsContained(current, range) && (condition is null || condition(current)))
            {
                nodes.Add(current);
                if (!all)
                {
                    current = NextNodeDescendants(current);
                    continue;
                }
            }

            current = NextNode(current);
        }

        return nodes;
    }

    private static List<Node> GetAllContainedNodes(DomRange range, Func<Node, bool>? condition = null) => GetContainedNodes(range, condition, all: true);

    // Elements

    private static bool IsHtmlElement([NotNullWhen(true)] Node? node) => node is Element { IsHtmlNamespace: true };

    private static bool IsHtmlElement([NotNullWhen(true)] Node? node, string name) => node is Element { IsHtmlNamespace: true } element && element.LocalName == name;

    private static bool IsHtmlElement([NotNullWhen(true)] Node? node, FrozenSet<string> names) => node is Element { IsHtmlNamespace: true } element && names.Contains(element.LocalName);

    private static FrozenSet<string> Names(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ListNames = Names("ol", "ul");
    private static readonly FrozenSet<string> ListItemNames = Names("li", "dt", "dd");
    private static readonly FrozenSet<string> DefinitionItemNames = Names("dd", "dt");
    private static readonly FrozenSet<string> TableCellNames = Names("td", "th");

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#prohibited-paragraph-child-name</summary>
    private static readonly FrozenSet<string> ProhibitedParagraphChildNames = Names(
        "address", "article", "aside", "blockquote", "caption", "center", "col", "colgroup", "dd", "details", "dir", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "li",
        "listing", "menu", "nav", "ol", "p", "plaintext", "pre", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead",
        "tr", "ul", "xmp");

    private static bool IsProhibitedParagraphChild(Node? node) => IsHtmlElement(node, ProhibitedParagraphChildNames);

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#the-directionality of the dir attributes of the element and of its ancestors.</summary>
    private static string GetDirectionality(Element element)
    {
        for (Node? current = element; IsHtmlElement(current); current = current.ParentNode)
        {
            var dir = ((Element)current).GetAttributeValue("dir");
            if (dir is not null && Infra.InfraAscii.EqualsIgnoreAsciiCase(dir, "ltr"))
                return "ltr";

            if (dir is not null && Infra.InfraAscii.EqualsIgnoreAsciiCase(dir, "rtl"))
                return "rtl";
        }

        return "ltr";
    }

    // Editability

    private static bool IsEditingHost([NotNullWhen(true)] Node? node) => node is not null && Editing.IsEditingHost(node);

    private static bool IsEditable([NotNullWhen(true)] Node? node) => node is not null && Editing.IsEditable(node);

    private static bool IsEditableOrEditingHost([NotNullWhen(true)] Node? node) => node is not null && (Editing.IsEditable(node) || Editing.IsEditingHost(node));

    private static bool HasEditableDescendants(Node node) => AnyDescendant(node, static descendant => Editing.IsEditable(descendant));

    private static Element? GetEditingHostOf(Node? node) => node is null ? null : Editing.GetEditingHost(node);

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#in-the-same-editing-host</summary>
    private static bool InSameEditingHost(Node? node1, Node? node2)
        => GetEditingHostOf(node1) is { } host && ReferenceEquals(host, GetEditingHostOf(node2));

    // Rendering

    private ComputedStyle GetStyle(Element element) => _engine.GetStyle(element);

    private Display GetDisplay(Element element) => _engine.GetStyle(element).Layout.Display;

    private bool IsDisplayNone(Node? node) => node is Element element && GetDisplay(element).IsNone;

    /// <summary>Whether the computed display of the element is <c>inline</c>.</summary>
    private bool IsDisplayInline(Element element) => GetDisplay(element).IsInlineBox;

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#block-node: a Document, a DocumentFragment, or an element whose
    /// display is not inline, inline-block, inline-table or none.
    /// </summary>
    private bool IsBlockNode([NotNullWhen(true)] Node? node) => node switch
    {
        Element element => GetDisplay(element) is var display && !display.IsNone && !display.IsInlineLevel,
        Document or DocumentFragment => true,
        _ => false,
    };

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#inline-node</summary>
    private bool IsInlineNode([NotNullWhen(true)] Node? node) => node is not null && !IsBlockNode(node);

    /// <summary>Whether the white space of the text of the element is preserved (white-space: pre, pre-wrap or break-spaces).</summary>
    private bool PreservesWhiteSpace(Node? parent)
        => parent is Element element && GetStyle(element).Layout.WhiteSpaceCollapse is WhiteSpaceCollapse.Preserve or WhiteSpaceCollapse.BreakSpaces or WhiteSpaceCollapse.PreserveSpaces;

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#whitespace-node</summary>
    private bool IsWhitespaceNode([NotNullWhen(true)] Node? node)
    {
        if (node is not Text text)
            return false;

        var data = text.Data;
        if (data.Length == 0)
            return true;

        if (text.ParentNode is not Element parent)
            return false;

        var collapse = GetStyle(parent).Layout.WhiteSpaceCollapse;
        if (collapse is WhiteSpaceCollapse.Collapse or WhiteSpaceCollapse.Discard)
            return data.AsSpan().IndexOfAnyExcept("\t\n\r ") < 0;

        if (collapse == WhiteSpaceCollapse.PreserveBreaks)
            return data.AsSpan().IndexOfAnyExcept("\t\r ") < 0;

        return false;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#collapsed-whitespace-node</summary>
    private bool IsCollapsedWhitespaceNode(Node? node)
    {
        if (!IsWhitespaceNode(node))
            return false;

        if (((Text)node).Data.Length == 0)
            return true;

        var ancestor = node.ParentNode;
        if (ancestor is null)
            return true;

        for (var current = ancestor; current is not null; current = current.ParentNode)
        {
            if (IsDisplayNone(current))
                return true;
        }

        while (!IsBlockNode(ancestor) && ancestor.ParentNode is { } parent)
        {
            ancestor = parent;
        }

        Node? reference = node;
        while (!ReferenceEquals(reference, ancestor))
        {
            reference = PreviousNode(reference);
            if (reference is null || IsBlockNode(reference) || IsHtmlElement(reference, "br"))
                return true;

            if ((reference is Text && !IsWhitespaceNode(reference)) || IsHtmlElement(reference, "img"))
                break;
        }

        reference = node;
        var stop = NextNodeDescendants(ancestor);
        while (!ReferenceEquals(reference, stop))
        {
            reference = NextNode(reference);
            if (reference is null || IsBlockNode(reference) || IsHtmlElement(reference, "br"))
                return true;

            if ((reference is Text && !IsWhitespaceNode(reference)) || IsHtmlElement(reference, "img"))
                break;
        }

        return false;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#visible</summary>
    private bool IsVisible([NotNullWhen(true)] Node? node)
    {
        if (node is null)
            return false;

        for (var current = node; current is not null; current = current.ParentNode)
        {
            if (IsDisplayNone(current))
                return false;
        }

        return IsVisibleCore(node);
    }

    private bool IsVisibleCore(Node node)
    {
        if (IsBlockNode(node) || (node is Text && !IsCollapsedWhitespaceNode(node)) || IsHtmlElement(node, "img") || (IsHtmlElement(node, "br") && !IsExtraneousLineBreak(node)))
            return true;

        // The specification only lists the img elements; the other replaced elements (e.g. iframe, embed) are rendered too
        if (node is Element element && BoxGeneration.IsReplaced(element, GetStyle(element)))
            return true;

        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (!IsDisplayNone(child) && IsVisibleCore(child))
                return true;
        }

        return false;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#invisible</summary>
    private bool IsInvisible([NotNullWhen(true)] Node? node) => node is not null && !IsVisible(node);

    private bool HasVisibleChild(Node node)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (IsVisible(child))
                return true;
        }

        return false;
    }

    private enum LineItem
    {
        /// <summary>The line ends: a block node, or the end of the block that contains the line.</summary>
        Boundary,

        /// <summary>A br element.</summary>
        LineBreak,

        /// <summary>A content of the line: text that is not collapsed, a replaced element or an atomic inline.</summary>
        Content,
    }

    /// <summary>
    /// The first item of the line of a node after it (or before it), in the block that contains it: the nodes that are not
    /// rendered, the white space that collapses and the inline elements without content are skipped.
    /// </summary>
    private LineItem FindLineItem(Node from, bool forward, Node? boundary = null)
    {
        var node = from;
        while (true)
        {
            // The next node in the direction, after the subtree of the current node: the inline ancestors are left until
            // the block that contains the line (or until the boundary element, when there is one)
            Node? next;
            while ((next = forward ? node.NextSibling : node.PreviousSibling) is null)
            {
                if (node.ParentNode is not Element parent || !IsDisplayInline(parent) || ReferenceEquals(parent, boundary))
                    return LineItem.Boundary;

                node = parent;
            }

            // The first item of the subtree of the node. When it has none, the search continues with the next node.
            node = next;
            while (true)
            {
                if (node is Text)
                {
                    if (!IsWhitespaceNode(node))
                        return LineItem.Content;

                    break;
                }

                if (node is not Element element)
                    break;

                var style = GetStyle(element);
                var display = style.Layout.Display;
                if (display.IsNone)
                    break;

                if (element.IsHtml(TagId.Br))
                    return LineItem.LineBreak;

                if (!display.IsInlineLevel)
                    return LineItem.Boundary;

                if (!display.IsInlineBox || BoxGeneration.IsReplaced(element, style))
                    return LineItem.Content;

                if ((forward ? element.FirstChild : element.LastChild) is not { } child)
                    break;

                node = child;
            }
        }
    }

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#collapsed-line-break: a br that begins a line box which has nothing
    /// else in it. As in the reference implementation of the specification (which checks that a character inserted after
    /// the br adds a line), it is a br that is not followed by another item on its line.
    /// </summary>
    private bool IsCollapsedLineBreak([NotNullWhen(true)] Node? node)
        => IsHtmlElement(node, "br") && FindLineItem(node, forward: true) == LineItem.Boundary;

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#extraneous-line-break: a br that has no visual effect (removing it
    /// would not change the layout), except the br that is the sole child of an li. It is a br that ends a line that has
    /// other contents: nothing follows it on its line, and a content precedes it.
    /// </summary>
    private bool IsExtraneousLineBreak([NotNullWhen(true)] Node? node)
    {
        if (!IsHtmlElement(node, "br"))
            return false;

        if (IsHtmlElement(node.ParentNode, "li") && node.ParentNode.ChildCount == 1)
            return false;

        return FindLineItem(node, forward: true) == LineItem.Boundary && FindLineItem(node, forward: false) == LineItem.Content;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#collapsed-block-prop</summary>
    private bool IsCollapsedBlockProp([NotNullWhen(true)] Node? node)
    {
        if (node is null)
            return false;

        if (IsCollapsedLineBreak(node) && !IsExtraneousLineBreak(node))
            return true;

        if (!IsInlineNode(node) || node is not Element)
            return false;

        var hasCollapsedBlockPropChild = false;
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            var isCollapsedBlockProp = IsCollapsedBlockProp(child);
            if (!IsInvisible(child) && !isCollapsedBlockProp)
                return false;

            hasCollapsedBlockPropChild |= isCollapsedBlockProp;
        }

        return hasCollapsedBlockPropChild;
    }

    // Ranges

    private void SetRange(Node startNode, int startOffset, Node endNode, int endOffset)
    {
        ActiveRange.SetStart(startNode, (uint)startOffset);
        ActiveRange.SetEnd(endNode, (uint)endOffset);
    }

    private void CollapseTo(Node node, int offset)
    {
        ActiveRange.SetStart(node, (uint)offset);
        ActiveRange.SetEnd(node, (uint)offset);
    }

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#preserving-ranges: moves a node to a new parent, at an index (-1 for
    /// the end), and keeps the boundary points of the active range at the same contents.
    /// </summary>
    private void MovePreservingRanges(Node node, Node newParent, int newIndex)
    {
        if (newIndex == -1)
        {
            newIndex = newParent.ChildCount;
        }

        var oldParent = node.ParentNode;
        var oldIndex = NodeIndex(node);
        var ranges = new List<DomRange>(1 + _extraRanges.Count);
        if (_activeRange is not null)
        {
            ranges.Add(_activeRange);
        }

        ranges.AddRange(_extraRanges);
        var points = new (Node Node, int Offset)[ranges.Count * 2];
        for (var i = 0; i < ranges.Count; i++)
        {
            points[2 * i] = (ranges[i].StartContainer, (int)ranges[i].StartOffset);
            points[(2 * i) + 1] = (ranges[i].EndContainer, (int)ranges[i].EndOffset);
        }

        for (var i = 0; i < points.Length; i++)
        {
            ref var point = ref points[i];
            if (ReferenceEquals(point.Node, newParent) && point.Offset > newIndex)
            {
                point.Offset++;
            }

            if (ReferenceEquals(point.Node, oldParent) && (point.Offset == oldIndex || point.Offset == oldIndex + 1))
            {
                point.Node = newParent;
                point.Offset += newIndex - oldIndex;
            }

            if (ReferenceEquals(point.Node, oldParent) && point.Offset > oldIndex + 1)
            {
                point.Offset--;
            }
        }

        newParent.InsertBefore(node, ChildAt(newParent, newIndex));
        for (var i = 0; i < ranges.Count; i++)
        {
            ranges[i].SetStart(points[2 * i].Node, (uint)Math.Min(points[2 * i].Offset, NodeLength(points[2 * i].Node)));
            ranges[i].SetEnd(points[(2 * i) + 1].Node, (uint)Math.Min(points[(2 * i) + 1].Offset, NodeLength(points[(2 * i) + 1].Node)));
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#set-the-tag-name</summary>
    private Element SetTagName(Element element, string newName)
    {
        if (IsHtmlElement(element, newName) || element.ParentNode is not { } parent)
            return element;

        var replacement = _document.CreateElement(newName);
        parent.InsertBefore(replacement, element);
        foreach (var attribute in element.Attributes.ToArray())
        {
            replacement.SetAttributeNS(attribute.NamespaceUri, attribute.Name, attribute.Value);
        }

        while (element.FirstChild is { } child)
        {
            MovePreservingRanges(child, replacement, -1);
        }

        parent.RemoveChild(element);
        return replacement;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#remove-extraneous-line-breaks-before</summary>
    private void RemoveExtraneousLineBreaksBefore(Node node)
    {
        var reference = node.PreviousSibling;
        if (reference is null)
            return;

        while (reference.LastChild is { } last)
        {
            reference = last;
        }

        while (IsInvisible(reference) && !IsExtraneousLineBreak(reference) && !ReferenceEquals(reference, node.ParentNode))
        {
            reference = PreviousNode(reference);
        }

        if (IsEditable(reference) && IsExtraneousLineBreak(reference))
        {
            reference.ParentNode!.RemoveChild(reference);
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#remove-extraneous-line-breaks-at-the-end-of</summary>
    private void RemoveExtraneousLineBreaksAtTheEndOf(Node node)
    {
        Node? reference = node;
        while (reference.LastChild is { } last)
        {
            reference = last;
        }

        while (IsInvisible(reference) && !IsExtraneousLineBreak(reference) && !ReferenceEquals(reference, node))
        {
            reference = PreviousNode(reference);
        }

        if (IsEditable(reference) && IsExtraneousLineBreak(reference))
        {
            while (IsEditable(reference.ParentNode) && IsInvisible(reference.ParentNode))
            {
                reference = reference.ParentNode;
            }

            reference.ParentNode!.RemoveChild(reference);
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#remove-extraneous-line-breaks-from</summary>
    private void RemoveExtraneousLineBreaksFrom(Node node)
    {
        RemoveExtraneousLineBreaksBefore(node);
        RemoveExtraneousLineBreaksAtTheEndOf(node);
    }

    private Element CreateElement(string name) => _document.CreateElement(name);

    private static void RemoveNode(Node node) => node.ParentNode?.RemoveChild(node);

    private static void InsertAfter(Node node, Node reference) => reference.ParentNode!.InsertBefore(node, reference.NextSibling);
}
