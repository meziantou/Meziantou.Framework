using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Dom;
using DomRange = Meziantou.Framework.WebPlatform.Dom.Range;

namespace Meziantou.Framework.WebPlatform.Html;

// The block formatting algorithms: https://w3c.github.io/editing/docs/execCommand/#block-formatting-command-definitions
internal sealed partial class EditingAlgorithms
{
    /// <summary>https://w3c.github.io/editing/docs/execCommand/#non-list-single-line-container</summary>
    private static readonly FrozenSet<string> NonListSingleLineContainerNames = Names("address", "div", "h1", "h2", "h3", "h4", "h5", "h6", "listing", "p", "pre", "xmp");

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#formattable-block-name, with the sectioning elements that the browsers
    /// accept too (article, aside, blockquote, footer, header, hgroup, nav, section), as the tests expect.
    /// </summary>
    private static readonly FrozenSet<string> FormattableBlockNames = Names(
        "address", "article", "aside", "blockquote", "dd", "div", "dt", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "nav", "p", "pre", "section");

    private static readonly FrozenSet<string> JustifyRemovedElementNames = Names("div", "span", "center");

    private static bool IsMarginProperty(string name) => StripVendorPrefix(name).StartsWith("margin", StringComparison.Ordinal);

    private static ReadOnlySpan<char> StripVendorPrefix(string name)
    {
        var span = name.AsSpan();
        if (span.Length > 1 && span[0] == '-' && span[1..].IndexOf('-') is > 0 and var end)
            return span[(end + 2)..];

        return span;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#indentation-element</summary>
    private static bool IsIndentationElement(Node? node)
    {
        if (IsHtmlElement(node, "blockquote"))
            return true;

        return IsHtmlElement(node, "div") && GetStylePropertyNames((Element)node).Exists(IsMarginProperty);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#simple-indentation-element</summary>
    private static bool IsSimpleIndentationElement(Node? node)
    {
        if (!IsIndentationElement(node))
            return false;

        var element = (Element)node!;
        foreach (var attribute in element.Attributes)
        {
            if (attribute.NamespaceUri is not null || attribute.LocalName is not ("style" or "dir"))
                return false;
        }

        foreach (var name in GetStylePropertyNames(element))
        {
            var property = StripVendorPrefix(name);
            if (!property.StartsWith("margin", StringComparison.Ordinal) && !property.StartsWith("border", StringComparison.Ordinal) && !property.StartsWith("padding", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool IsNonListSingleLineContainer(Node? node) => IsHtmlElement(node, NonListSingleLineContainerNames);

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#single-line-container</summary>
    private static bool IsSingleLineContainer(Node? node) => IsNonListSingleLineContainer(node) || IsHtmlElement(node, ListItemNames);

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#block-node-of</summary>
    private Node? GetBlockNodeOf(Node? node)
    {
        while (IsInlineNode(node))
        {
            node = node.ParentNode;
        }

        return node;
    }

    /// <summary>Whether the node has no ancestor in the same editing host of which it is an allowed child.</summary>
    private static bool HasNoAllowedAncestor(Node node)
    {
        for (var ancestor = node.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
        {
            if (InSameEditingHost(node, ancestor) && IsAllowedChild(node, ancestor))
                return false;
        }

        return true;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#fix-disallowed-ancestors</summary>
    private void FixDisallowedAncestors(Node? node)
    {
        if (!IsEditable(node))
            return;

        if (HasNoAllowedAncestor(node))
        {
            if (IsHtmlElement(node, DefinitionItemNames))
            {
                Wrap([node], static sibling => IsHtmlElement(sibling, "dl") && ((Element)sibling).AttributeCount == 0, () => CreateElement("dl"));
                return;
            }

            if (GetEditingHostOf(node) is not { } host || !IsAllowedChild("p", host))
                return;

            if (!IsProhibitedParagraphChild(node))
                return;

            node = SetTagName((Element)node, _state.DefaultSingleLineContainerName);
            FixDisallowedAncestors(node);
            foreach (var child in GetChildren(node))
            {
                if (!IsProhibitedParagraphChild(child))
                    continue;

                var childValues = RecordValues([child]);
                SplitParent([child]);
                RestoreValues(childValues);
            }

            return;
        }

        var values = RecordValues([node]);
        while (node.ParentNode is { } parent && !IsAllowedChild(node, parent))
        {
            if (!IsEditable(parent) || parent.ParentNode is null)
                break;

            SplitParent([node]);
        }

        RestoreValues(values);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#normalize-sublists</summary>
    private void NormalizeSublists(Node? item)
    {
        if (!IsHtmlElement(item, "li") || !IsEditable(item) || !IsEditable(item.ParentNode))
            return;

        Element? newItem = null;
        while (HasListChild(item))
        {
            var child = item.LastChild!;
            if (IsHtmlElement(child, ListNames) || (newItem is null && child is Text text && text.Data.AsSpan().IndexOfAnyExcept(" \t\n\f\r") < 0))
            {
                newItem = null;
                MovePreservingRanges(child, item.ParentNode!, 1 + NodeIndex(item));
            }
            else
            {
                if (newItem is null)
                {
                    newItem = CreateElement("li");
                    InsertAfter(newItem, item);
                }

                MovePreservingRanges(child, newItem, 0);
            }
        }

        static bool HasListChild(Node node)
        {
            for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            {
                if (IsHtmlElement(child, ListNames))
                    return true;
            }

            return false;
        }
    }

    /// <summary>The state of the selection for the list commands: "none", "ol", "ul", "mixed", "mixed ol" or "mixed ul".</summary>
    private string GetSelectionListState()
    {
        if (_activeRange is null)
            return "none";

        var newRange = BlockExtend(_activeRange);
        var nodeList = GetContainedNodes(newRange, static node => IsEditable(node) && !IsIndentationElement(node)
            && (IsHtmlElement(node, ListNames) || IsHtmlElement(node.ParentNode, ListNames) || IsAllowedChild(node, "li")));
        if (nodeList.Count == 0)
            return "none";

        if (nodeList.TrueForAll(static node => IsInList(node, "ol")) && !nodeList.Exists(static node => ContainsList(node, "ul")))
            return "ol";

        if (nodeList.TrueForAll(static node => IsInList(node, "ul")) && !nodeList.Exists(static node => ContainsList(node, "ol")))
            return "ul";

        var hasOl = nodeList.Exists(static node => IsInList(node, "ol") || ContainsList(node, "ol"));
        var hasUl = nodeList.Exists(static node => IsInList(node, "ul") || ContainsList(node, "ul"));
        return hasOl && hasUl ? "mixed" : hasOl ? "mixed ol" : hasUl ? "mixed ul" : "none";

        static bool IsInList(Node node, string name)
            => IsHtmlElement(node, name) || IsHtmlElement(node.ParentNode, name) || (IsHtmlElement(node.ParentNode, "li") && IsHtmlElement(node.ParentNode.ParentNode, name));

        static bool ContainsList(Node node, string name)
            => IsHtmlElement(node, name) || AnyDescendant(node, descendant => IsHtmlElement(descendant, name));
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#alignment-value</summary>
    private string GetAlignmentValue(Node? node)
    {
        while (node is not null && (node is not Element element || GetDisplay(element) is var display && (display.IsNone || display.IsInlineBox)))
        {
            node = node.ParentNode;
        }

        if (node is not Element block)
            return "left";

        var value = GetStyle(block).GetComputedValue("text-align-all");
        if (value.StartsWith("-webkit-", StringComparison.Ordinal) || value.StartsWith("-moz-", StringComparison.Ordinal))
        {
            value = value[(value.IndexOf('-', 1, StringComparison.Ordinal) + 1)..];
        }

        return value switch
        {
            "start" or "auto" or "match-parent" => GetDirectionality(block) == "ltr" ? "left" : "right",
            "end" => GetDirectionality(block) == "ltr" ? "right" : "left",
            "center" or "justify" or "left" or "right" => value,
            _ => "left",
        };
    }

    // Equivalent points: https://w3c.github.io/editing/docs/execCommand/#equivalent-point

    private (Node Node, int Offset)? GetNextEquivalentPoint(Node node, int offset)
    {
        if (NodeLength(node) == 0)
            return null;

        if (offset == NodeLength(node) && node.ParentNode is { } parent && IsInlineNode(node))
            return (parent, 1 + NodeIndex(node));

        // The elements that are deleted as a whole (e.g. the elements that are not editable) are not entered
        if (ChildAt(node, offset) is { } child && NodeLength(child) != 0 && IsInlineNode(child) && !IsAtomicElement(child))
            return (child, 0);

        return null;
    }

    private (Node Node, int Offset)? GetPreviousEquivalentPoint(Node node, int offset)
    {
        if (NodeLength(node) == 0)
            return null;

        if (offset == 0 && node.ParentNode is { } parent && IsInlineNode(node))
            return (parent, NodeIndex(node));

        if (ChildAt(node, offset - 1) is { } child && NodeLength(child) != 0 && IsInlineNode(child) && !IsAtomicElement(child))
            return (child, NodeLength(child));

        return null;
    }

    private (Node Node, int Offset) GetFirstEquivalentPoint(Node node, int offset)
    {
        while (GetPreviousEquivalentPoint(node, offset) is { } previous)
        {
            (node, offset) = previous;
        }

        return (node, offset);
    }

    private (Node Node, int Offset) GetLastEquivalentPoint(Node node, int offset)
    {
        while (GetNextEquivalentPoint(node, offset) is { } next)
        {
            (node, offset) = next;
        }

        return (node, offset);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#block-start-point</summary>
    private bool IsBlockStartPoint(Node node, int offset)
        => (node.ParentNode is null && offset == 0)
            || (ChildAt(node, offset - 1) is { } child && IsVisible(child) && (IsBlockNode(child) || IsHtmlElement(child, "br")));

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#block-end-point</summary>
    private bool IsBlockEndPoint(Node node, int offset)
        => (node.ParentNode is null && offset == NodeLength(node))
            || (ChildAt(node, offset) is { } child && IsVisible(child) && IsBlockNode(child));

    private bool IsBlockBoundaryPoint(Node node, int offset) => IsBlockStartPoint(node, offset) || IsBlockEndPoint(node, offset);

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#block-extend</summary>
    private DomRange BlockExtend(DomRange range)
    {
        var startNode = range.StartContainer;
        var startOffset = (int)range.StartOffset;
        var endNode = range.EndContainer;
        var endOffset = (int)range.EndOffset;
        if (GetOutermostListItem(startNode) is { ParentNode: { } startItemParent } startItem)
        {
            startOffset = NodeIndex(startItem);
            startNode = startItemParent;
        }

        if (!IsBlockStartPoint(startNode, startOffset))
        {
            do
            {
                if (startOffset == 0)
                {
                    if (startNode.ParentNode is null)
                        break;

                    startOffset = NodeIndex(startNode);
                    startNode = startNode.ParentNode;
                }
                else
                {
                    startOffset--;
                }
            }
            while (!IsBlockBoundaryPoint(startNode, startOffset));
        }

        while (startOffset == 0 && startNode.ParentNode is { } parent)
        {
            startOffset = NodeIndex(startNode);
            startNode = parent;
        }

        if (GetOutermostListItem(endNode) is { ParentNode: { } endItemParent } endItem)
        {
            endOffset = 1 + NodeIndex(endItem);
            endNode = endItemParent;
        }

        if (!IsBlockEndPoint(endNode, endOffset))
        {
            do
            {
                if (endOffset == NodeLength(endNode))
                {
                    if (endNode.ParentNode is null)
                        break;

                    endOffset = 1 + NodeIndex(endNode);
                    endNode = endNode.ParentNode;
                }
                else
                {
                    endOffset++;
                }
            }
            while (!IsBlockBoundaryPoint(endNode, endOffset));
        }

        while (endOffset == NodeLength(endNode) && endNode.ParentNode is { } parent)
        {
            endOffset = 1 + NodeIndex(endNode);
            endNode = parent;
        }

        var newRange = _document.CreateRange();
        newRange.SetStart(startNode, (uint)startOffset);
        newRange.SetEnd(endNode, (uint)endOffset);
        return newRange;

        // The li that is the inclusive ancestor of the node the closest to the root
        static Node? GetOutermostListItem(Node node)
        {
            Node? item = null;
            for (Node? current = node; current is not null; current = current.ParentNode)
            {
                if (IsHtmlElement(current, "li"))
                {
                    item = current;
                }
            }

            return item;
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#follows-a-line-break</summary>
    private bool FollowsLineBreak(Node node)
    {
        var offset = 0;
        while (!IsBlockBoundaryPoint(node, offset))
        {
            if (ChildAt(node, offset - 1) is { } previous && IsVisible(previous))
                return false;

            if (offset == 0 || !node.HasChildNodes())
            {
                if (node.ParentNode is null)
                    return true;

                offset = NodeIndex(node);
                node = node.ParentNode;
            }
            else
            {
                node = ChildAt(node, offset - 1)!;
                offset = NodeLength(node);
            }
        }

        return true;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#precedes-a-line-break</summary>
    private bool PrecedesLineBreak(Node node)
    {
        var offset = NodeLength(node);
        while (!IsBlockBoundaryPoint(node, offset))
        {
            if (ChildAt(node, offset) is { } next && IsVisible(next))
                return false;

            if (offset == NodeLength(node) || !node.HasChildNodes())
            {
                if (node.ParentNode is null)
                    return true;

                offset = 1 + NodeIndex(node);
                node = node.ParentNode;
            }
            else
            {
                node = ChildAt(node, offset)!;
                offset = 0;
            }
        }

        return true;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#split-the-parent</summary>
    private void SplitParent(List<Node> nodeList)
    {
        if (nodeList.Count == 0)
            return;

        var originalParent = nodeList[0].ParentNode;
        if (!IsEditable(originalParent) || originalParent.ParentNode is null)
            return;

        var containsFirstChild = nodeList.Contains(originalParent.FirstChild!);
        var containsLastChild = nodeList.Contains(originalParent.LastChild!);
        if (containsFirstChild)
        {
            RemoveExtraneousLineBreaksBefore(originalParent);
        }

        var followsLineBreak = containsFirstChild && FollowsLineBreak(originalParent);
        var precedesLineBreak = containsLastChild && PrecedesLineBreak(originalParent);
        if (!containsFirstChild && containsLastChild)
        {
            for (var i = nodeList.Count - 1; i >= 0; i--)
            {
                MovePreservingRanges(nodeList[i], originalParent.ParentNode, 1 + NodeIndex(originalParent));
            }

            if (precedesLineBreak && !PrecedesLineBreak(nodeList[^1]))
            {
                InsertAfter(CreateElement("br"), nodeList[^1]);
            }

            RemoveExtraneousLineBreaksAtTheEndOf(originalParent);
            return;
        }

        if (!containsFirstChild)
        {
            var clonedParent = (Element)originalParent.CloneNode(subtree: false);
            (originalParent as Element)?.RemoveAttribute("id");
            originalParent.ParentNode.InsertBefore(clonedParent, originalParent);
            while (nodeList[0].PreviousSibling is not null)
            {
                MovePreservingRanges(originalParent.FirstChild!, clonedParent, -1);
            }
        }

        foreach (var node in nodeList)
        {
            MovePreservingRanges(node, originalParent.ParentNode, NodeIndex(originalParent));
        }

        if (followsLineBreak && !FollowsLineBreak(nodeList[0]))
        {
            nodeList[0].ParentNode!.InsertBefore(CreateElement("br"), nodeList[0]);
        }

        if (IsInlineNode(nodeList[^1]) && !IsHtmlElement(nodeList[^1], "br") && IsHtmlElement(originalParent.FirstChild, "br") && !IsInlineNode(originalParent))
        {
            originalParent.RemoveChild(originalParent.FirstChild);
        }

        if (!originalParent.HasChildNodes())
        {
            RemoveNode(originalParent);
            if (precedesLineBreak && !PrecedesLineBreak(nodeList[^1]))
            {
                InsertAfter(CreateElement("br"), nodeList[^1]);
            }
        }
        else
        {
            RemoveExtraneousLineBreaksBefore(originalParent);
        }

        if (nodeList[^1].NextSibling is null && nodeList[^1].ParentNode is { } newParent)
        {
            RemoveExtraneousLineBreaksAtTheEndOf(newParent);
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#remove-a-node-preserving-its-descendants</summary>
    private void RemovePreservingDescendants(Node node)
    {
        if (node.HasChildNodes())
        {
            SplitParent(GetChildren(node));
        }
        else
        {
            RemoveNode(node);
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#indent</summary>
    private void IndentNodes(List<Node> nodeList)
    {
        if (nodeList.Count == 0)
            return;

        var firstNode = nodeList[0];
        if (IsHtmlElement(firstNode.ParentNode, ListNames))
        {
            var tag = ((Element)firstNode.ParentNode).LocalName;
            Wrap(nodeList, node => IsHtmlElement(node, tag), () => CreateElement(tag));
            return;
        }

        var newParent = Wrap(nodeList, static node => IsSimpleIndentationElement(node), () => CreateElement("blockquote"));
        FixDisallowedAncestors(newParent);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#outdent</summary>
    private void OutdentNode(Node node)
    {
        if (!IsEditable(node))
            return;

        if (IsSimpleIndentationElement(node))
        {
            RemovePreservingDescendants(node);
            return;
        }

        if (IsIndentationElement(node))
        {
            var element = (Element)node;
            element.RemoveAttribute("dir");
            if (element.HasAttributeWithLocalName("style"))
            {
                var style = element.GetInlineStyle();
                style.RemoveProperty("margin");
                style.RemoveProperty("padding");
                style.RemoveProperty("border");
                RemoveEmptyStyleAttribute(element);
            }

            SetTagName(element, "div");
            return;
        }

        var currentAncestor = node.ParentNode;
        var ancestorList = new List<Node>();
        while (IsEditable(currentAncestor) && currentAncestor is Element && !IsSimpleIndentationElement(currentAncestor) && !IsHtmlElement(currentAncestor, ListNames))
        {
            ancestorList.Add(currentAncestor);
            currentAncestor = currentAncestor.ParentNode;
        }

        if (!IsEditable(currentAncestor) || !IsSimpleIndentationElement(currentAncestor))
        {
            currentAncestor = node.ParentNode;
            ancestorList = [];
            while (IsEditable(currentAncestor) && currentAncestor is Element && !IsIndentationElement(currentAncestor) && !IsHtmlElement(currentAncestor, ListNames))
            {
                ancestorList.Add(currentAncestor);
                currentAncestor = currentAncestor.ParentNode;
            }
        }

        if (IsHtmlElement(node, ListNames) && (!IsEditable(currentAncestor) || !IsIndentationElement(currentAncestor)))
        {
            var list = (Element)node;
            list.RemoveAttribute("reversed");
            list.RemoveAttribute("start");
            list.RemoveAttribute("type");
            var children = GetChildren(list);
            if (list.AttributeCount > 0 && !IsHtmlElement(list.ParentNode, ListNames))
            {
                SetTagName(list, "div");
            }
            else
            {
                var values = RecordValues(GetChildren(list));
                RemovePreservingDescendants(list);
                RestoreValues(values);
            }

            foreach (var child in children)
            {
                FixDisallowedAncestors(child);
            }

            return;
        }

        if (!IsEditable(currentAncestor) || !IsIndentationElement(currentAncestor))
            return;

        ancestorList.Add(currentAncestor);
        var originalAncestor = currentAncestor;
        while (ancestorList.Count > 0)
        {
            currentAncestor = ancestorList[^1];
            ancestorList.RemoveAt(ancestorList.Count - 1);
            var target = ReferenceEquals(node.ParentNode, currentAncestor) ? node : ancestorList[^1];
            if (IsInlineNode(target) && !IsHtmlElement(target, "br") && IsHtmlElement(target.NextSibling, "br"))
            {
                RemoveNode(target.NextSibling);
            }

            var precedingSiblings = new List<Node>();
            var followingSiblings = new List<Node>();
            var following = false;
            for (var child = currentAncestor.FirstChild; child is not null; child = child.NextSibling)
            {
                if (ReferenceEquals(child, target))
                {
                    following = true;
                }
                else
                {
                    (following ? followingSiblings : precedingSiblings).Add(child);
                }
            }

            IndentNodes(precedingSiblings);
            IndentNodes(followingSiblings);
        }

        OutdentNode(originalAncestor);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#toggle-lists</summary>
    private void ToggleLists(string tagName)
    {
        var mode = GetSelectionListState() == tagName ? "disable" : "enable";
        var range = ActiveRange;
        var otherTagName = tagName == "ol" ? "ul" : "ol";
        var items = new List<Node>();
        for (Node? ancestor = range.StartContainer; ancestor is not null; ancestor = ancestor.ParentNode)
        {
            if (IsHtmlElement(ancestor, "li"))
            {
                items.Insert(0, ancestor);
            }
        }

        var common = range.CommonAncestorContainer;
        var endItems = new List<Node>();
        for (Node? ancestor = range.EndContainer; ancestor is not null && !ReferenceEquals(ancestor, common); ancestor = ancestor.ParentNode)
        {
            if (IsHtmlElement(ancestor, "li"))
            {
                endItems.Insert(0, ancestor);
            }
        }

        // The items of the start node come first, the root first, as in the reference implementation
        items.AddRange(endItems);
        foreach (var item in items)
        {
            NormalizeSublists(item);
        }

        var newRange = BlockExtend(range);
        if (mode == "enable")
        {
            foreach (var list in GetAllContainedNodes(newRange, node => IsEditable(node) && IsHtmlElement(node, otherTagName)))
            {
                if ((IsEditable(list.PreviousSibling) && IsHtmlElement(list.PreviousSibling, tagName)) || (IsEditable(list.NextSibling) && IsHtmlElement(list.NextSibling, tagName)))
                {
                    var children = GetChildren(list);
                    var values = RecordValues(children);
                    SplitParent(children);
                    Wrap(children, node => IsHtmlElement(node, tagName));
                    RestoreValues(values);
                }
                else
                {
                    SetTagName((Element)list, tagName);
                }
            }
        }

        var nodeList = GetContainedNodes(newRange, static node => IsEditable(node) && !IsIndentationElement(node)
            && (IsHtmlElement(node, ListNames) || IsHtmlElement(node.ParentNode, ListNames) || IsAllowedChild(node, "li")));
        if (mode == "enable")
        {
            nodeList.RemoveAll(static node => IsHtmlElement(node, ListNames) && !IsHtmlElement(node.ParentNode, ListNames));
        }

        var index = 0;
        if (mode == "disable")
        {
            while (index < nodeList.Count)
            {
                var sublist = new List<Node> { nodeList[index++] };
                if (IsHtmlElement(sublist[0], tagName))
                {
                    OutdentNode(sublist[0]);
                    continue;
                }

                while (index < nodeList.Count && ReferenceEquals(nodeList[index], sublist[^1].NextSibling) && !IsHtmlElement(nodeList[index], tagName))
                {
                    sublist.Add(nodeList[index++]);
                }

                var values = RecordValues(sublist);
                SplitParent(sublist);
                foreach (var node in sublist)
                {
                    FixDisallowedAncestors(node);
                }

                RestoreValues(values);
            }

            return;
        }

        while (index < nodeList.Count)
        {
            var sublist = new List<Node>();
            while (sublist.Count == 0 || (index < nodeList.Count && ReferenceEquals(nodeList[index], sublist[^1].NextSibling)))
            {
                if (IsHtmlElement(nodeList[index], "p") || IsHtmlElement(nodeList[index], "div"))
                {
                    sublist.Add(SetTagName((Element)nodeList[index++], "li"));
                }
                else if (IsHtmlElement(nodeList[index], "li") || IsHtmlElement(nodeList[index], ListNames))
                {
                    sublist.Add(nodeList[index++]);
                }
                else
                {
                    var nodesToWrap = new List<Node>();
                    while (nodesToWrap.Count == 0
                        || (index < nodeList.Count && ReferenceEquals(nodeList[index], nodesToWrap[^1].NextSibling) && IsInlineNode(nodeList[index]) && IsInlineNode(nodesToWrap[^1]) && !IsHtmlElement(nodesToWrap[^1], "br")))
                    {
                        nodesToWrap.Add(nodeList[index++]);
                    }

                    // The nodes that are not wrapped (they are all invisible) are not items of the list
                    if (Wrap(nodesToWrap, siblingCriteria: null, () => CreateElement("li")) is { } wrapper)
                    {
                        sublist.Add(wrapper);
                    }
                    else if (sublist.Count == 0)
                    {
                        break;
                    }
                }
            }

            if (sublist.Count == 0)
                continue;

            if (IsHtmlElement(sublist[0].ParentNode, tagName) || sublist.TrueForAll(static node => IsHtmlElement(node, ListNames)))
                continue;

            if (IsHtmlElement(sublist[0].ParentNode, otherTagName))
            {
                var values = RecordValues(sublist);
                SplitParent(sublist);
                Wrap(sublist, node => IsHtmlElement(node, tagName), () => CreateElement(tagName));
                RestoreValues(values);
                continue;
            }

            FixDisallowedAncestors(Wrap(sublist, node => IsHtmlElement(node, tagName), () =>
            {
                var parent = sublist[0].ParentNode;
                if (!IsEditable(parent) || !IsSimpleIndentationElement(parent) || !IsEditable(parent.PreviousSibling) || !IsHtmlElement(parent.PreviousSibling, tagName))
                    return CreateElement(tagName);

                var list = parent.PreviousSibling;
                NormalizeSublists(list.LastChild);
                if (!IsEditable(list.LastChild) || !IsHtmlElement(list.LastChild, tagName))
                {
                    list.AppendChild(CreateElement(tagName));
                }

                return (Element)list.LastChild!;
            }));
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#justify-the-selection</summary>
    private void JustifySelection(string alignment)
    {
        var newRange = BlockExtend(ActiveRange);
        var elementList = GetAllContainedNodes(newRange, static node => node is Element element && IsEditable(node)
            && (element.HasAttributeWithLocalName("align") || GetInlineStyleValue(element, "text-align").Length > 0 || IsHtmlElement(node, "center")));
        foreach (var node in elementList)
        {
            var element = (Element)node;
            element.RemoveAttribute("align");
            if (element.HasAttributeWithLocalName("style"))
            {
                element.GetInlineStyle().RemoveProperty("text-align");
                RemoveEmptyStyleAttribute(element);
            }

            if (IsHtmlElement(element, JustifyRemovedElementNames) && element.AttributeCount == 0)
            {
                RemovePreservingDescendants(element);
            }

            if (IsHtmlElement(element, "center") && element.AttributeCount > 0)
            {
                SetTagName(element, "div");
            }
        }

        newRange = BlockExtend(ActiveRange);
        var nodeList = GetContainedNodes(newRange, node => IsEditable(node) && IsAllowedChild(node, "div") && GetAlignmentValue(node) != alignment);
        var index = 0;
        while (index < nodeList.Count)
        {
            var sublist = new List<Node> { nodeList[index++] };
            while (index < nodeList.Count && ReferenceEquals(nodeList[index], sublist[^1].NextSibling))
            {
                sublist.Add(nodeList[index++]);
            }

            Wrap(
                sublist,
                node =>
                {
                    if (!IsHtmlElement(node, "div"))
                        return false;

                    var element = (Element)node;
                    foreach (var attribute in element.Attributes)
                    {
                        var matches = (attribute.Name == "align" && Infra.InfraAscii.EqualsIgnoreAsciiCase(attribute.Value, alignment))
                            || (attribute.Name == "style" && GetStylePropertyNames(element).Count == 1 && GetInlineStyleValue(element, "text-align") == alignment);
                        if (!matches)
                            return false;
                    }

                    return true;
                },
                () =>
                {
                    var newParent = CreateElement("div");
                    newParent.SetAttribute("style", "text-align: " + alignment);
                    return newParent;
                });
        }
    }
}
