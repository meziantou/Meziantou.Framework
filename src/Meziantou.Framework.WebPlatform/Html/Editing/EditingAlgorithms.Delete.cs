using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

// The deletion algorithms: https://w3c.github.io/editing/docs/execCommand/#deleting-the-selection
internal sealed partial class EditingAlgorithms
{
    private static readonly System.Collections.Frozen.FrozenSet<string> TableSectionNames = Names("thead", "tbody", "tfoot", "tr", "th", "td");
    private static readonly System.Collections.Frozen.FrozenSet<string> ListContainerNames = Names("ol", "ul", "dl");

    private const char NoBreakSpaceCharacter = (char)0xA0;
    private const string NoBreakSpace = "\U000000A0";

    /// <summary>The combining marks that are deleted with the character they follow (the combining diacritical marks and the Hebrew points).</summary>
    private static bool IsCombiningMark(char c) => (int)c is (>= 0x0300 and <= 0x036F) or (>= 0x0591 and <= 0x05BD) or 0x05C1 or 0x05C2;

    /// <summary>A state override (a boolean) or a value override (a string) of a command.</summary>
    private readonly record struct CommandOverride(string Command, bool? State, string? Value);

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#record-current-overrides</summary>
    private List<CommandOverride> RecordCurrentOverrides()
    {
        var overrides = new List<CommandOverride>();
        if (_activeRange is null)
            return overrides;

        if (_state.GetValueOverride(_activeRange, "createlink") is { } link)
        {
            overrides.Add(new("createlink", null, link));
        }

        foreach (var command in (ReadOnlySpan<string>)["bold", "italic", "strikethrough", "subscript", "superscript", "underline"])
        {
            if (_state.GetStateOverride(_activeRange, command) is { } state)
            {
                overrides.Add(new(command, state, null));
            }
        }

        foreach (var command in (ReadOnlySpan<string>)["fontname", "fontsize", "forecolor", "hilitecolor"])
        {
            if (_state.GetValueOverride(_activeRange, command) is { } value)
            {
                overrides.Add(new(command, null, value));
            }
        }

        return overrides;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#record-current-states-and-values</summary>
    private List<CommandOverride> RecordCurrentStatesAndValues()
    {
        var overrides = new List<CommandOverride>();
        var node = GetAllEffectivelyContainedNodes(ActiveRange).Find(IsFormattableNode);
        if (node is null)
            return overrides;

        overrides.Add(new("createlink", null, GetEffectiveCommandValue(node, "createlink")));
        foreach (var command in (ReadOnlySpan<string>)["bold", "italic", "strikethrough", "subscript", "superscript", "underline"])
        {
            overrides.Add(new(command, IsActivatedValue(command, GetEffectiveCommandValue(node, command)), null));
        }

        foreach (var command in (ReadOnlySpan<string>)["fontname", "fontsize", "forecolor", "hilitecolor"])
        {
            overrides.Add(new(command, null, GetCommandValue(command)));
        }

        overrides.Add(new("fontsize", null, GetEffectiveCommandValue(node, "fontsize")));
        return overrides;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#restore-states-and-values</summary>
    private void RestoreStatesAndValues(List<CommandOverride> overrides)
    {
        var range = ActiveRange;
        var node = GetAllEffectivelyContainedNodes(range).Find(IsFormattableNode);
        if (node is null)
        {
            foreach (var (command, state, value) in overrides)
            {
                if (state is { } stateOverride)
                {
                    _state.SetStateOverride(range, command, stateOverride);
                }

                if (value is not null)
                {
                    _state.SetValueOverride(range, command, value);
                }
            }

            return;
        }

        foreach (var (command, state, value) in overrides)
        {
            if (state is { } stateOverride)
            {
                if (QueryState(command) == stateOverride)
                    continue;

                RunAction(command, "");
            }
            else if (value is null)
            {
                continue;
            }
            else if (command == "createlink")
            {
                var linkOverride = _state.GetValueOverride(range, "createlink");
                if (linkOverride is not null ? linkOverride == value : GetEffectiveCommandValue(node, "createlink") == value)
                    continue;

                RunAction("createlink", value);
            }
            else if (command == "fontsize")
            {
                var sizeOverride = _state.GetValueOverride(range, "fontsize");
                if (sizeOverride is not null ? sizeOverride == value : AreLooselyEquivalentValues(command, GetEffectiveCommandValue(node, "fontsize"), value))
                    continue;

                if (GetLegacyFontSize(value) is not { } legacySize)
                    continue;

                RunAction("fontsize", legacySize);
            }
            else
            {
                if (AreEquivalentValues(command, QueryValue(command), value))
                    continue;

                RunAction(command, value);
            }

            node = GetAllEffectivelyContainedNodes(range).Find(IsFormattableNode) ?? node;
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#canonical-space-sequence</summary>
    private static string CanonicalSpaceSequence(int length, bool nonBreakingStart, bool nonBreakingEnd)
    {
        if (length == 0)
            return "";

        if (length == 1)
            return !nonBreakingStart && !nonBreakingEnd ? " " : NoBreakSpace;

        var buffer = new StringBuilder(length);
        var repeatedPair = nonBreakingStart ? NoBreakSpace + " " : " " + NoBreakSpace;
        while (length > 3)
        {
            buffer.Append(repeatedPair);
            length -= 2;
        }

        if (length == 3)
        {
            buffer.Append(!nonBreakingStart && !nonBreakingEnd ? " " + NoBreakSpace + " "
                : nonBreakingStart && !nonBreakingEnd ? NoBreakSpace + NoBreakSpace + " "
                : !nonBreakingStart ? " " + NoBreakSpace + NoBreakSpace
                : NoBreakSpace + " " + NoBreakSpace);
        }
        else if (buffer.Length > 0 && !nonBreakingStart && !nonBreakingEnd)
        {
            // The specification ends the sequence with a no-break space followed by a space (4 spaces give a space, two
            // no-break spaces and a space). As in the browsers, the spaces keep alternating
            buffer.Append(" " + NoBreakSpace);
        }
        else
        {
            buffer.Append(!nonBreakingEnd ? NoBreakSpace + " " : !nonBreakingStart ? " " + NoBreakSpace : NoBreakSpace + NoBreakSpace);
        }

        return buffer.ToString();
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#canonicalize-whitespace</summary>
    /// <param name="replaceSpaces">False to only remove the spaces that collapse (<paramref name="fixCollapsedSpace"/>): the sequence of white space is not replaced with the canonical one.</param>
    private void CanonicalizeWhitespace(Node node, int offset, bool fixCollapsedSpace = true, bool replaceSpaces = true)
    {
        if (!IsEditableOrEditingHost(node))
            return;

        var startNode = node;
        var startOffset = offset;
        while (true)
        {
            if (ChildAt(startNode, startOffset - 1) is { } previous && InSameEditingHost(startNode, previous))
            {
                startNode = previous;
                startOffset = NodeLength(startNode);
            }
            else if (startOffset == 0 && !FollowsLineBreak(startNode) && InSameEditingHost(startNode, startNode.ParentNode))
            {
                startOffset = NodeIndex(startNode);
                startNode = startNode.ParentNode!;
            }
            else if (startNode is Text text && !PreservesWhiteSpace(text.ParentNode) && startOffset != 0 && text.Data[startOffset - 1] is ' ' or NoBreakSpaceCharacter)
            {
                startOffset--;
            }
            else
            {
                break;
            }
        }

        var endNode = startNode;
        var endOffset = startOffset;
        var length = 0;
        var collapseSpaces = startOffset == 0 && FollowsLineBreak(startNode);
        while (true)
        {
            if (ChildAt(endNode, endOffset) is { } next && InSameEditingHost(endNode, next))
            {
                endNode = next;
                endOffset = 0;
            }
            else if (endOffset == NodeLength(endNode) && !PrecedesLineBreak(endNode) && InSameEditingHost(endNode, endNode.ParentNode))
            {
                endOffset = 1 + NodeIndex(endNode);
                endNode = endNode.ParentNode!;
            }
            else if (endNode is Text text && !PreservesWhiteSpace(text.ParentNode) && endOffset != NodeLength(endNode) && text.Data[endOffset] is ' ' or NoBreakSpaceCharacter)
            {
                if (fixCollapsedSpace && collapseSpaces && text.Data[endOffset] == ' ')
                {
                    text.DeleteData((uint)endOffset, 1);
                    continue;
                }

                collapseSpaces = text.Data[endOffset] == ' ';
                endOffset++;
                length++;
            }
            else
            {
                break;
            }
        }

        if (fixCollapsedSpace)
        {
            while (ComparePoints(startNode, startOffset, endNode, endOffset) < 0)
            {
                if (ChildAt(endNode, endOffset - 1) is { } previous && InSameEditingHost(endNode, previous))
                {
                    endNode = previous;
                    endOffset = NodeLength(endNode);
                }
                else if (endOffset == 0 && InSameEditingHost(endNode, endNode.ParentNode))
                {
                    endOffset = NodeIndex(endNode);
                    endNode = endNode.ParentNode!;
                }
                else if (endNode is Text text && !PreservesWhiteSpace(text.ParentNode) && endOffset == NodeLength(endNode) && text.Data.EndsWith(' ', StringComparison.Ordinal) && PrecedesLineBreak(endNode))
                {
                    endOffset--;
                    length--;
                    text.DeleteData((uint)endOffset, 1);
                }
                else
                {
                    break;
                }
            }
        }

        if (!replaceSpaces)
            return;

        // As in the browsers, the white space next to an element that is not editable (contenteditable=false) starts or ends
        // with a no-break space: it stays visible when the scripts change or remove the content of the element
        var replacementWhitespace = CanonicalSpaceSequence(
            length,
            (startOffset == 0 && FollowsLineBreak(startNode)) || IsNonEditableUnit(ChildAt(startNode, startOffset - 1)),
            (endOffset == NodeLength(endNode) && PrecedesLineBreak(endNode)) || IsNonEditableUnit(ChildAt(endNode, endOffset)));
        var replacementIndex = 0;
        while (ComparePoints(startNode, startOffset, endNode, endOffset) < 0)
        {
            if (ChildAt(startNode, startOffset) is { } child)
            {
                startNode = child;
                startOffset = 0;
            }
            else if (startNode is not Text || startOffset == NodeLength(startNode))
            {
                startOffset = 1 + NodeIndex(startNode);
                startNode = startNode.ParentNode!;
            }
            else
            {
                var text = (Text)startNode;
                if (replacementIndex < replacementWhitespace.Length)
                {
                    var character = replacementWhitespace[replacementIndex++];
                    if (character != text.Data[startOffset])
                    {
                        // The character is replaced without moving the boundary points that are after it
                        text.InsertData((uint)startOffset, character.ToString());
                        text.DeleteData((uint)startOffset + 1, 1);
                    }
                }

                startOffset++;
            }
        }
    }

    /// <summary>
    /// As in the browsers, a boundary point of the range in the document element (e.g. after selecting the children of an
    /// editable html element) is moved into the body element: the head and the body element are never deleted, only the
    /// content of the body element is.
    /// </summary>
    private void ClampRangeToBody(Dom.Range range)
    {
        if (_document.DocumentElement is not { } root || _document.Body is not { } body || !ReferenceEquals(body.ParentNode, root))
            return;

        var bodyIndex = NodeIndex(body);
        var startsBeforeBody = ReferenceEquals(range.StartContainer, root) && (int)range.StartOffset <= bodyIndex;
        var endsAfterBody = ReferenceEquals(range.EndContainer, root) && (int)range.EndOffset > bodyIndex;
        if (startsBeforeBody && (endsAfterBody || body.Contains(range.EndContainer)))
        {
            ActiveRange.SetStart(body, 0);
        }

        if (endsAfterBody && (startsBeforeBody || body.Contains(range.StartContainer)))
        {
            ActiveRange.SetEnd(body, (uint)body.ChildCount);
        }
    }

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#delete-the-selection: deletes the contents of the active range, and
    /// merges the blocks of its boundary points.
    /// </summary>
    /// <param name="canonicalizeWhitespace">False when the caller handles the white space around the deletion (see <see cref="DeleteSelectionAndRebalance"/>).</param>
    private void DeleteSelection(bool blockMerging = true, bool stripWrappers = true, bool forward = true, bool canonicalizeWhitespace = true)
    {
if (_activeRange is null)
            return;

        var range = _activeRange;
        ClampRangeToBody(range);
        if (canonicalizeWhitespace)
        {
            CanonicalizeWhitespace(range.StartContainer, (int)range.StartOffset);
            CanonicalizeWhitespace(range.EndContainer, (int)range.EndOffset);
        }

        var (startNode, startOffset) = GetLastEquivalentPoint(range.StartContainer, (int)range.StartOffset);
        var (endNode, endOffset) = GetFirstEquivalentPoint(range.EndContainer, (int)range.EndOffset);
        if (ComparePoints(endNode, endOffset, startNode, startOffset) <= 0)
        {
            range.Collapse(toStart: forward);
            return;
        }

        if (startNode is Text && startOffset == 0)
        {
            startOffset = NodeIndex(startNode);
            startNode = startNode.ParentNode!;
        }

        if (endNode is Text && endOffset == NodeLength(endNode))
        {
            endOffset = 1 + NodeIndex(endNode);
            endNode = endNode.ParentNode!;
        }

        SetRange(startNode, startOffset, endNode, endOffset);
        var startBlock = FindBlockForMerging(range.StartContainer);
        var endBlock = FindBlockForMerging(range.EndContainer);

        // Whether the range starts where a line of the start block starts
        var startsLine = blockMerging && IsAncestor(startBlock, endBlock)
            && FindLineItemAt(startNode, startOffset, forward: false) != LineItem.Content && FindLineItemAt(startNode, startOffset, forward: true) != LineItem.Boundary;
        var overrides = RecordCurrentStatesAndValues();
        if (ReferenceEquals(startNode, endNode) && IsEditable(startNode) && startNode is Text sameText)
        {
            sameText.DeleteData((uint)startOffset, (uint)(endOffset - startOffset));
            if (canonicalizeWhitespace)
            {
                CanonicalizeWhitespace(startNode, startOffset, fixCollapsedSpace: false);
            }

            range.Collapse(toStart: forward);
            RestoreStatesAndValues(overrides);
            return;
        }

        if (IsEditable(startNode) && startNode is Text startText)
        {
            startText.DeleteData((uint)startOffset, (uint)(NodeLength(startNode) - startOffset));
        }

        var nodeList = GetContainedNodes(range, static node => (IsEditable(node) || IsNonEditableUnit(node)) && !IsHtmlElement(node, TableSectionNames));
        foreach (var node in nodeList)
        {
            var parent = node.ParentNode;
            if (parent is null)
                continue;

            parent.RemoveChild(node);

            // The specification appends the br to the parent of the node before it removes the empty inline ancestors. As in
            // the browsers (and as the tests expect), the wrappers that are stripped are removed first: the br is a child of
            // the block
            if (stripWrappers || (!IsAncestor(parent, startNode) && !ReferenceEquals(parent, startNode)))
            {
                while (IsEditable(parent) && IsInlineNode(parent) && NodeLength(parent) == 0)
                {
                    var grandparent = parent.ParentNode!;
                    grandparent.RemoveChild(parent);
                    parent = grandparent;
                }
            }

            // As in the browsers, an inline editing host that becomes empty gets no br element (its block is not editable)
            if (GetBlockNodeOf(parent) is { } block && !HasVisibleChild(block) && IsEditableOrEditingHost(parent) && IsEditableOrEditingHost(block))
            {
                parent.AppendChild(CreateElement("br"));
            }
        }

        if (IsEditable(endNode) && endNode is Text endText)
        {
            endText.DeleteData(0, (uint)endOffset);
        }

        if (canonicalizeWhitespace)
        {
            CanonicalizeWhitespace(range.StartContainer, (int)range.StartOffset, fixCollapsedSpace: false);
            CanonicalizeWhitespace(range.EndContainer, (int)range.EndOffset, fixCollapsedSpace: false);
        }

        if (!blockMerging || startBlock is null || endBlock is null || !InSameEditingHost(startBlock, endBlock) || ReferenceEquals(startBlock, endBlock))
        {
            range.Collapse(toStart: forward);
            RestoreStatesAndValues(overrides);
            return;
        }

        var removesEmptyEndBlock = IsEditable(endBlock) && !IsAncestor(startBlock, endBlock) && !IsDescendant(startBlock, endBlock) && !HasLineContent(endBlock);
        if (!removesEmptyEndBlock && startBlock.ChildCount == 1 && IsCollapsedBlockProp(startBlock.FirstChild))
        {
            startBlock.RemoveChild(startBlock.FirstChild);
        }

        List<(Node Node, string Command, string? Value)>? values;
        if (removesEmptyEndBlock)
        {
            // The specification moves the nodes of an empty end block (e.g. an empty span element before its br element)
            // to the start block. As in the browsers, they are removed with the block: the start block is not changed,
            // and the caret is at the start of its last line
            while (endBlock.FirstChild is { } child)
            {
                endBlock.RemoveChild(child);
            }

            RemoveEmptyBlocks(endBlock, stop: null);
            CollapseTo(startBlock, NodeLength(startBlock) - (IsHtmlElement(startBlock.LastChild, "br") ? 1 : 0));
            values = RecordValues([]);
        }
        else if (IsAncestor(startBlock, endBlock))
        {
            var referenceNode = endBlock;
            while (!ReferenceEquals(referenceNode.ParentNode, startBlock))
            {
                referenceNode = referenceNode.ParentNode!;
            }

            CollapseTo(startBlock, NodeIndex(referenceNode));
            if (!endBlock.HasChildNodes())
            {
                while (IsEditable(endBlock) && endBlock.ParentNode!.ChildCount == 1 && !ReferenceEquals(endBlock.ParentNode, startBlock))
                {
                    var parent = endBlock.ParentNode;
                    parent.RemoveChild(endBlock);
                    endBlock = parent;
                }

                if (IsEditable(endBlock) && !IsInlineNode(endBlock) && IsInlineNode(endBlock.PreviousSibling) && IsInlineNode(endBlock.NextSibling))
                {
                    InsertAfter(CreateElement("br"), endBlock);
                }

                if (IsEditable(endBlock))
                {
                    RemoveNode(endBlock);
                }

                RestoreStatesAndValues(overrides);
                return;
            }

            if (!IsInlineNode(endBlock.FirstChild))
            {
                RestoreStatesAndValues(overrides);
                return;
            }

            if (startsLine)
            {
                // The specification moves the first line of the end block to the line of the start block where the range
                // starts. As in the browsers (and as the tests expect), when the range starts at the start of this line,
                // the lines before the end block are deleted and the end block keeps its first line: the caret is in it
                CollapseTo(endBlock, 0);
                RestoreStatesAndValues(overrides);
                return;
            }

            if (MovesFirstLineOnly(startBlock, endBlock, 0))
            {
                // The first line of the end block is moved before it, with its white-space
                var joinOffset = NodeIndex(referenceNode);
                foreach (var node in ExtractFirstLine(endBlock, 0, startBlock))
                {
                    startBlock.InsertBefore(node, referenceNode);
                }

                RemoveEmptyBlocks(endBlock, startBlock);
                CollapseTo(startBlock, joinOffset);
                FinishMerge();
                return;
            }

            var children = new List<Node> { endBlock.FirstChild };
            while (!IsHtmlElement(children[^1], "br") && IsInlineNode(children[^1].NextSibling))
            {
                children.Add(children[^1].NextSibling!);
            }

            values = RecordValues(children);
            while (!ReferenceEquals(children[0].ParentNode, startBlock))
            {
                var parent = children[0].ParentNode;
                SplitParent(children);
                if (ReferenceEquals(children[0].ParentNode, parent))
                    break;
            }

            if (IsEditable(children[0].PreviousSibling) && IsHtmlElement(children[0].PreviousSibling, "br"))
            {
                RemoveNode(children[0].PreviousSibling!);
            }
        }
        else if (IsDescendant(startBlock, endBlock))
        {
            CollapseTo(startBlock, NodeLength(startBlock));
            var referenceNode = startBlock;
            while (!ReferenceEquals(referenceNode.ParentNode, endBlock))
            {
                referenceNode = referenceNode.ParentNode!;
            }

            if (IsInlineNode(referenceNode.NextSibling) && IsHtmlElement(startBlock.LastChild, "br"))
            {
                startBlock.RemoveChild(startBlock.LastChild);
            }

            if (MovesFirstLineOnly(startBlock, endBlock, 1 + NodeIndex(referenceNode)))
            {
                // The line that follows the start block is moved in it, with its white-space
                var joinOffset = NodeLength(startBlock);
                foreach (var node in ExtractFirstLine(endBlock, 1 + NodeIndex(referenceNode), startBlock))
                {
                    startBlock.AppendChild(node);
                }

                CollapseTo(startBlock, joinOffset);
                FinishMerge();
                return;
            }

            var nodesToMove = new List<Node>();
            if (referenceNode.NextSibling is { } firstToMove && !IsBlockNode(firstToMove))
            {
                nodesToMove.Add(firstToMove);
            }

            while (nodesToMove.Count > 0 && !IsHtmlElement(nodesToMove[^1], "br") && nodesToMove[^1].NextSibling is { } nextToMove && !IsBlockNode(nextToMove))
            {
                nodesToMove.Add(nextToMove);
            }

            values = RecordValues(nodesToMove);
            foreach (var node in nodesToMove)
            {
                MovePreservingRanges(node, startBlock, -1);
            }
        }
        else
        {
            CollapseTo(startBlock, NodeLength(startBlock));
            if (IsEditable(startBlock) && !HasVisibleChild(startBlock) && HasLineContent(endBlock) && GetIndentationDepth(endBlock) > GetIndentationDepth(startBlock))
            {
                // The specification moves the contents of the end block to the start block. As in the browsers (and as
                // the tests expect), a start block that the deletion empties is removed instead when the end block, which
                // is not empty, is more indented (e.g. an item of a nested list, or an item of a list after a
                // paragraph): the end block keeps its position, and the caret is in it
                while (startBlock.FirstChild is { } child)
                {
                    startBlock.RemoveChild(child);
                }

                RemoveEmptyBlocks(startBlock, stop: null);
                CollapseTo(endBlock, 0);
                RestoreStatesAndValues(overrides);
                return;
            }

            if (IsInlineNode(endBlock.FirstChild) && IsHtmlElement(startBlock.LastChild, "br"))
            {
                startBlock.RemoveChild(startBlock.LastChild);
            }

            if (MovesFirstLineOnly(startBlock, endBlock, 0))
            {
                // The specification moves the contents of the end block to the start block. When their white-space differ,
                // as in the browsers (and as the tests expect), only the first line of the end block is moved, in elements
                // that keep its white-space
                var joinOffset = NodeLength(startBlock);
                foreach (var node in ExtractFirstLine(endBlock, 0, startBlock))
                {
                    startBlock.AppendChild(node);
                }

                RemoveEmptyBlocks(endBlock, stop: null);
                CollapseTo(startBlock, joinOffset);
                FinishMerge();
                return;
            }

            values = RecordValues(GetChildren(endBlock));
            while (endBlock.FirstChild is { } child)
            {
                MovePreservingRanges(child, startBlock, -1);
            }

            while (!endBlock.HasChildNodes() && endBlock.ParentNode is { } parent)
            {
                parent.RemoveChild(endBlock);
                endBlock = parent;
            }
        }

        // The lists that are now adjacent are merged
        Node? ancestor = startBlock;
        while (FindMergeableList(ancestor) is { } list)
        {
            var next = list.NextSibling!;
            while (next.FirstChild is { } child)
            {
                MovePreservingRanges(child, list, -1);
            }

            RemoveNode(next);
            ancestor = list;
        }

        RestoreValues(values);
        FinishMerge();

        void FinishMerge()
        {
            if (!startBlock.HasChildNodes())
            {
                startBlock.AppendChild(CreateElement("br"));
            }

            RemoveExtraneousLineBreaksAtTheEndOf(startBlock);
            RestoreStatesAndValues(overrides);
        }

        // The closest inclusive ancestor in the same editing host that is an ol (or ul) whose next sibling is an ol (or ul) in
        // the same editing host
        Node? FindMergeableList(Node? from)
        {
            for (var current = from; current is not null; current = current.ParentNode)
            {
                if (!InSameEditingHost(from, current))
                    continue;

                if (((IsHtmlElement(current, "ol") && IsHtmlElement(current.NextSibling, "ol")) || (IsHtmlElement(current, "ul") && IsHtmlElement(current.NextSibling, "ul")))
                    && InSameEditingHost(from, current.NextSibling))
                {
                    return current;
                }
            }

            return null;
        }
    }

    /// <summary>Whether a node has a child that is rendered, other than the line break that keeps an empty block (a collapsed block prop).</summary>
    private bool HasLineContent(Node node)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (IsVisible(child) && !IsCollapsedBlockProp(child))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the spaces that collapse at the end of the line before the active range, when the range starts at the end of
    /// this line (before a block or a line feed that white-space preserves, not before a br element), and at the start of
    /// the line after it, when the range ends at the start of this line: as in the browsers, the spaces that are not
    /// rendered around a line break do not become visible when it is deleted (the specification replaces them with
    /// no-break spaces).
    /// </summary>
    private void RemoveCollapsedSpacesAroundLineBreaks(Dom.Range range)
    {
        if (FindLineItemAt(range.EndContainer, (int)range.EndOffset, forward: false) != LineItem.Content
            && FindTextAfter(range.EndContainer, (int)range.EndOffset, removeEmptyTextNodes: false) is var (after, afterOffset) && IsEditable(after) && !PreservesWhiteSpace(after.ParentNode))
        {
            var data = after.Data;
            var preservesLineFeeds = PreservesLineFeeds(GetWhiteSpace(after));
            var end = afterOffset;
            while (end < data.Length && (data[end] is ' ' or '\t' or '\r' || (data[end] == '\n' && !preservesLineFeeds)))
            {
                end++;
            }

            if (end > afterOffset)
            {
                after.DeleteData((uint)afterOffset, (uint)(end - afterOffset));
            }
        }

        // The space before a br element is kept: it is rendered once the br element is deleted
        var next = FindLineItemAt(range.StartContainer, (int)range.StartOffset, forward: true);
        var endsLine = next == LineItem.Boundary
            || (next == LineItem.LineBreak && range.StartContainer is Text startText && startText.Data.AsSpan((int)range.StartOffset).TrimStart(" \t\r").StartsWith("\n"));
        if (endsLine
            && FindTextBefore(range.StartContainer, (int)range.StartOffset, removeEmptyTextNodes: false) is var (before, beforeOffset) && IsEditable(before) && !PreservesWhiteSpace(before.ParentNode))
        {
            var data = before.Data;
            var preservesLineFeeds = PreservesLineFeeds(GetWhiteSpace(before));
            var start = beforeOffset;
            while (start > 0 && (data[start - 1] is ' ' or '\t' or '\r' || (data[start - 1] == '\n' && !preservesLineFeeds)))
            {
                start--;
            }

            if (start < beforeOffset)
            {
                before.DeleteData((uint)start, (uint)(beforeOffset - start));
            }
        }
    }

    /// <summary>The number of lists and of indentation elements (e.g. blockquote) that contain a node in its editing host.</summary>
    private static int GetIndentationDepth(Node node)
    {
        var depth = 0;
        for (var ancestor = node.ParentNode; ancestor is not null && IsEditable(ancestor); ancestor = ancestor.ParentNode)
        {
            if (IsHtmlElement(ancestor, ListContainerNames) || IsIndentationElement(ancestor))
            {
                depth++;
            }
        }

        return depth;
    }

    /// <summary>
    /// The range that a deletion deletes when the range of the selection starts before a list or a list item that has
    /// contents and ends after the last contents of its editing host; else null. As in the browsers (and as the tests expect), the contents of the
    /// items are deleted, from the start of the first item to the end of the last one: the first item is kept, empty (the
    /// specification removes the list items that the range contains).
    /// </summary>
    private (Node StartNode, int StartOffset, Node EndNode, int EndOffset)? GetRangeInListItems(Node startNode, int startOffset, Node endNode, int endOffset)
    {
        if (startNode is Text || ChildAt(startNode, startOffset) is not { } first || !IsEditable(first) || (!IsHtmlElement(first, ListContainerNames) && !IsHtmlElement(first, ListItemNames)))
            return null;

        if (endNode is Text || ChildAt(endNode, endOffset - 1) is not { } last)
            return null;

        // Nothing follows the end of the range in the editing host
        for (var current = ChildAt(endNode, endOffset) ?? NextNodeDescendants(endNode); current is not null && IsEditable(current); current = NextNodeDescendants(current))
        {
            if (IsVisible(current))
                return null;
        }

        // The items have contents (a list whose items are empty is removed)
        var range = _document.CreateRange();
        range.SetStart(startNode, (uint)startOffset);
        range.SetEnd(endNode, (uint)endOffset);
        if (GetAllContainedNodes(range, node => (node is Text && !IsWhitespaceNode(node)) || IsAtomicElement(node)).Count == 0)
            return null;

        while (first is Element { FirstChild: { } child } && IsEditable(first) && !IsHtmlElement(first, "br") && !IsAtomicElement(first))
        {
            first = child;
        }

        while (last is Element { LastChild: { } child } && IsEditable(last) && !IsHtmlElement(last, "br") && !IsAtomicElement(last))
        {
            last = child;
        }

        var start = first is Text ? (first, 0) : (first.ParentNode!, NodeIndex(first));
        var end = last is Text ? (last, NodeLength(last)) : (last.ParentNode!, 1 + NodeIndex(last));
        if (ComparePoints(start.Item1, start.Item2, end.Item1, end.Item2) >= 0)
            return null;

        return (start.Item1, start.Item2, end.Item1, end.Item2);
    }

    /// <summary>
    /// The target range of the beforeinput event of an edit of the user (see <see cref="Editing.UserEdit"/>), from the range
    /// of the selection, or the caret extended in the direction of a deletion. As in the browsers,
    /// <list type="bullet">
    /// <item>a deletion backward at a caret in a Text node deletes the previous code point (not the grapheme cluster);</item>
    /// <item>the range of a deletion at a caret that deletes a line break or an atomic inline (e.g. an img) is the range of this node in its parent;</item>
    /// <item>a boundary point of the range of a selection that is before or after a Text node (that is not white space) is in this Text node, and the range in list items is the one that is deleted (see <see cref="GetRangeInListItems"/>).</item>
    /// </list>
    /// </summary>
    public (Node StartNode, int StartOffset, Node EndNode, int EndOffset) GetTargetRange(Node startNode, int startOffset, Node endNode, int endOffset, Node? caretNode, int caretOffset, bool deletion, bool backward, bool character)
    {
        if (caretNode is null)
        {
            // The range of a selection
            if (deletion && GetRangeInListItems(startNode, startOffset, endNode, endOffset) is { } rangeInListItems)
                return rangeInListItems;

            if (startNode is not Text && ChildAt(startNode, startOffset) is Text startText && !IsWhitespaceNode(startText))
            {
                (startNode, startOffset) = (startText, 0);
            }

            if (endNode is not Text && ChildAt(endNode, endOffset - 1) is Text endText && !IsWhitespaceNode(endText))
            {
                (endNode, endOffset) = (endText, NodeLength(endText));
            }

            return (startNode, startOffset, endNode, endOffset);
        }

        // The deletion removes the blocks of the only empty line of the editing host (see EmptyEditingHostWithoutVisibleContent)
        if (deletion && GetEditingHostOf(caretNode) is { } host && GetOnlyEmptyLineBlock(host) is { } line)
            return (host, 0, line, NodeLength(line));

        if (backward && character && caretNode is Text caretText && caretOffset > 0 && caretOffset <= NodeLength(caretText) && caretText.Data[caretOffset - 1] is not (' ' or '\t' or '\n' or '\r'))
        {
            var start = caretOffset - 1;
            if (start > 0 && char.IsLowSurrogate(caretText.Data[start]) && char.IsHighSurrogate(caretText.Data[start - 1]))
            {
                start--;
            }

            return (caretText, start, caretText, caretOffset);
        }

        // The node that follows the start of the range, or precedes its end, when it is a line break or an atomic inline
        // (not for a deletion forward that joins the block of the caret to a block that is not one of its ancestors or
        // descendants: the range starts at the caret)
        if (startNode is Text text && text.Data.AsSpan(Math.Min(startOffset, text.Data.Length)).IndexOfAnyExcept(" \t\n\r") < 0 && !PreservesWhiteSpace(text.ParentNode)
            && text.NextSibling is Element next && (IsHtmlElement(next, "br") || IsAtomicElement(next))
            && (backward || IsAncestor(GetBlockNodeOf(endNode), text) || IsAncestor(GetBlockNodeOf(text), endNode)))
        {
            (startNode, startOffset) = (text.ParentNode!, NodeIndex(next));
        }

        if (endNode is Text && endOffset == 0 && endNode.PreviousSibling is Element previous && (IsHtmlElement(previous, "br") || IsAtomicElement(previous)))
        {
            (endNode, endOffset) = (endNode.ParentNode!, 1 + NodeIndex(previous));
        }

        return (startNode, startOffset, endNode, endOffset);
    }

    /// <summary>The white space characters that the browsers rebalance after a deletion: the white space that collapses and the no-break spaces.</summary>
    private static bool IsRebalancedWhiteSpace(char c) => c is ' ' or NoBreakSpaceCharacter;

    /// <summary>
    /// Deletes the contents of the active range for the delete and forwardDelete commands. The specification canonicalizes
    /// the white space around the deletion (the whole sequence of spaces and no-break spaces, through the inline elements).
    /// The browsers do not: as they do (and as the tests expect), the spaces that collapse are removed first, then, after the
    /// deletion,
    /// <list type="bullet">
    /// <item>the spaces that are not rendered around a line break that is deleted are removed (see <see cref="RemoveCollapsedSpacesAroundLineBreaks"/>);</item>
    /// <item>the space that was before the deleted contents is replaced with a no-break space when it now ends its line;</item>
    /// <item>the white space around the caret is rebalanced in its Text node only (see <see cref="RebalanceWhiteSpace"/>):
    /// the Text node that follows the caret when it is at the end of its node, the one that precedes it when none follows;</item>
    /// <item>a br element is inserted when the deletion empties a line that other contents of its block follow or precede
    /// (the specification only inserts it in a block that becomes empty);</item>
    /// <item>the caret is in a Text node when one is adjacent to it (the one before it first).</item>
    /// </list>
    /// </summary>
    private void DeleteSelectionAndRebalance(bool forward = true)
    {
        if (_activeRange is null)
            return;

        var range = _activeRange;
        if (GetRangeInListItems(range.StartContainer, (int)range.StartOffset, range.EndContainer, (int)range.EndOffset) is var (itemsStartNode, itemsStartOffset, itemsEndNode, itemsEndOffset))
        {
            SetRange(itemsStartNode, itemsStartOffset, itemsEndNode, itemsEndOffset);
        }

        CanonicalizeWhitespace(range.StartContainer, (int)range.StartOffset, replaceSpaces: false);
        CanonicalizeWhitespace(range.EndContainer, (int)range.EndOffset, replaceSpaces: false);
        if (!range.Collapsed)
        {
            RemoveCollapsedSpacesAroundLineBreaks(range);
        }

        var (startNode, startOffset) = GetLastEquivalentPoint(range.StartContainer, (int)range.StartOffset);
        var (endNode, endOffset) = GetFirstEquivalentPoint(range.EndContainer, (int)range.EndOffset);
        if (ComparePoints(endNode, endOffset, startNode, startOffset) <= 0)
        {
            range.Collapse(toStart: forward);
            return;
        }

        // The space before the deleted contents, when it is rendered
        (Text Text, int Offset)? leadingSpace = null;
        if (GetFirstEquivalentPoint(startNode, startOffset) is (Text leadingText, > 0 and var leadingEnd)
            && leadingText.Data[leadingEnd - 1] == ' ' && !PreservesWhiteSpace(leadingText.ParentNode) && IsEditable(leadingText)
            && FindLineItemAt(leadingText, leadingEnd - 1, forward: false) == LineItem.Content)
        {
            leadingSpace = (leadingText, leadingEnd - 1);
        }

        var emptiesLine = FindLineItemAt(startNode, startOffset, forward: false) != LineItem.Content && FindLineItemAt(endNode, endOffset, forward: true) == LineItem.Boundary
            && FindLineItemAt(startNode, startOffset, forward: true) == LineItem.Content && IsInOneLine(startNode, startOffset, endNode, endOffset);
        var startBlock = FindBlockForMerging(startNode);
        var endBlock = FindBlockForMerging(endNode);
        var joinsBlocks = startBlock is not null && endBlock is not null && !ReferenceEquals(startBlock, endBlock);
        DeleteSelection(forward: forward, canonicalizeWhitespace: false);
        if (!range.Collapsed)
            return;

        var rebalanced = RebalanceWhiteSpaceAtCaret(joinsBlocks);
        if (leadingSpace is var (spaceText, spaceOffset) && spaceText.ParentNode is not null && !ReferenceEquals(spaceText, rebalanced)
            && spaceOffset == NodeLength(spaceText) - 1 && spaceText.Data[spaceOffset] == ' ' && PrecedesLineBreak(spaceText))
        {
            ReplaceCharacter(spaceText, spaceOffset, NoBreakSpaceCharacter);
        }

        var caretNode = range.StartContainer;
        var caretOffset = (int)range.StartOffset;
        if (emptiesLine && IsEditableOrEditingHost(caretNode is Text ? caretNode.ParentNode : caretNode) && IsEditableOrEditingHost(GetBlockNodeOf(caretNode))
            && FindLineItemAt(caretNode, caretOffset, forward: false) != LineItem.Content && FindLineItemAt(caretNode, caretOffset, forward: true) == LineItem.Boundary)
        {
            // The line of the caret is empty, and is not the only line of its block: a br element keeps it
            var lineBreak = CreateElement("br");
            if (caretNode is Text)
            {
                caretNode.ParentNode!.InsertBefore(lineBreak, caretOffset == 0 ? caretNode : caretNode.NextSibling);
            }
            else
            {
                caretNode.InsertBefore(lineBreak, ChildAt(caretNode, caretOffset));
                CollapseTo(caretNode, caretOffset);
            }

            return;
        }

        // The caret is in the Text node that is before it (the empty Text nodes between them are removed), or after it. The
        // overrides of the commands are the ones of the caret: they are kept.
        var overrides = RecordCurrentOverrides();
        if (FindTextBefore(caretNode, caretOffset, removeEmptyTextNodes: true) is var (previousText, previousOffset) && ReferenceEquals(previousText.ParentNode, range.StartContainer is Text ? range.StartContainer.ParentNode : range.StartContainer))
        {
            CollapseTo(previousText, previousOffset);
        }
        else if (range.StartContainer is not Text && ChildAt(range.StartContainer, (int)range.StartOffset) is Text { Length: > 0 } nextText)
        {
            CollapseTo(nextText, 0);
        }

        foreach (var (command, state, value) in overrides)
        {
            if (state is { } stateOverride)
            {
                _state.SetStateOverride(range, command, stateOverride);
            }

            if (value is not null)
            {
                _state.SetValueOverride(range, command, value);
            }
        }
    }

    /// <summary>
    /// Rebalances the white space around the caret (see <see cref="RebalanceWhiteSpace"/>), in the Text node that follows
    /// it when it is at the end of its node (the empty Text nodes between them are removed), else in the one that precedes
    /// it. Returns the Text node.
    /// </summary>
    private Text? RebalanceWhiteSpaceAtCaret(bool acrossSiblings)
    {
        var range = ActiveRange;
        var point = FindTextAfter(range.StartContainer, (int)range.StartOffset, removeEmptyTextNodes: true)
            ?? FindTextBefore(range.StartContainer, (int)range.StartOffset, removeEmptyTextNodes: false);
        if (point is not var (text, offset))
            return null;

        RebalanceWhiteSpace(text, offset, acrossSiblings);
        return text;
    }

    /// <summary>Whether the contents of a range are inline contents of one line: the range contains no line break (a br element, a line feed that white-space preserves) and no block.</summary>
    private bool IsInOneLine(Node startNode, int startOffset, Node endNode, int endOffset)
    {
        if (!ReferenceEquals(GetBlockNodeOf(startNode), GetBlockNodeOf(endNode)))
            return false;

        if (startNode is Text startText && PreservesLineFeeds(GetWhiteSpace(startText))
            && startText.Data.AsSpan(startOffset, (ReferenceEquals(startNode, endNode) ? endOffset : NodeLength(startText)) - startOffset).Contains('\n'))
        {
            return false;
        }

        if (endNode is Text endText && !ReferenceEquals(startNode, endNode) && PreservesLineFeeds(GetWhiteSpace(endText)) && endText.Data.AsSpan(0, endOffset).Contains('\n'))
            return false;

        var range = _document.CreateRange();
        range.SetStart(startNode, (uint)startOffset);
        range.SetEnd(endNode, (uint)endOffset);
        return GetAllContainedNodes(range, node => IsHtmlElement(node, "br") || IsBlockNode(node) || (node is Text text && text.Data.Contains('\n', StringComparison.Ordinal) && PreservesLineFeeds(GetWhiteSpace(text)))).Count == 0;
    }

    /// <summary>
    /// The start of the Text node that follows a point, through the boundaries of the inline elements: null when the point
    /// is in a Text node (not at its end) or when another node follows it (a line break, an atomic inline, a block...).
    /// A point in a Text node, not at its end, is returned as is.
    /// </summary>
    private (Text Text, int Offset)? FindTextAfter(Node node, int offset, bool removeEmptyTextNodes)
    {
        if (node is Text start && offset < NodeLength(start))
            return (start, offset);

        var host = GetEditingHostOf(node);
        while (true)
        {
            if (node is Text || ChildAt(node, offset) is not { } child)
            {
                // The end of the node
                if (node.ParentNode is not { } parent || !IsInlineNode(node) || ReferenceEquals(node, host))
                    return null;

                offset = 1 + NodeIndex(node);
                node = parent;
            }
            else if (child is Text text)
            {
                if (text.Length != 0)
                    return (text, 0);

                if (removeEmptyTextNodes && IsEditable(text))
                {
                    node.RemoveChild(text);
                }
                else
                {
                    offset++;
                }
            }
            else if (child is Element && IsInlineNode(child) && !IsDisplayNone(child) && !IsHtmlElement(child, "br") && !IsAtomicElement(child) && IsEditable(child))
            {
                node = child;
                offset = 0;
            }
            else if (child is Element)
            {
                return null;
            }
            else
            {
                offset++;
            }
        }
    }

    /// <summary>The end of the Text node that precedes a point, through the boundaries of the inline elements (see <see cref="FindTextAfter"/>). A point in a Text node, not at its start, is returned as is.</summary>
    private (Text Text, int Offset)? FindTextBefore(Node node, int offset, bool removeEmptyTextNodes)
    {
        if (node is Text start && offset > 0)
            return (start, offset);

        var host = GetEditingHostOf(node);
        while (true)
        {
            if (node is Text || ChildAt(node, offset - 1) is not { } child)
            {
                // The start of the node
                if (node.ParentNode is not { } parent || !IsInlineNode(node) || ReferenceEquals(node, host))
                    return null;

                offset = NodeIndex(node);
                node = parent;
            }
            else if (child is Text text)
            {
                if (text.Length != 0)
                    return (text, (int)text.Length);

                if (removeEmptyTextNodes && IsEditable(text))
                {
                    node.RemoveChild(text);
                }

                offset--;
            }
            else if (child is Element && IsInlineNode(child) && !IsDisplayNone(child) && !IsHtmlElement(child, "br") && !IsAtomicElement(child) && IsEditable(child))
            {
                node = child;
                offset = NodeLength(child);
            }
            else if (child is Element)
            {
                return null;
            }
            else
            {
                offset--;
            }
        }
    }

    /// <summary>
    /// Rebalances the sequence of white space (spaces and no-break spaces) around an offset of a Text node whose white
    /// space collapses, as the browsers do after a deletion: a single space is a space, and the spaces of a longer sequence
    /// alternate with no-break spaces, from a no-break space; a space that starts or ends the Text node is a no-break space
    /// (the white space of the other nodes is not known to be visible), unless a Text node that starts with another
    /// character follows it.
    /// </summary>
    /// <param name="acrossSiblings">Whether the Text nodes that are adjacent siblings are rebalanced as one Text node (the lines that a deletion joins).</param>
    private void RebalanceWhiteSpace(Text text, int offset, bool acrossSiblings)
    {
        if (!IsEditable(text) || PreservesWhiteSpace(text.ParentNode))
            return;

        // The Text nodes of the sequence, and the offset in their concatenation
        var texts = new List<Text> { text };
        if (acrossSiblings)
        {
            while (texts[0].PreviousSibling is Text previous && IsEditable(previous))
            {
                texts.Insert(0, previous);
                offset += (int)previous.Length;
            }

            while (texts[^1].NextSibling is Text next && IsEditable(next))
            {
                texts.Add(next);
            }
        }

        var data = texts.Count == 1 ? text.Data : string.Concat(texts.Select(static t => t.Data));
        var preservesLineFeeds = PreservesLineFeeds(GetWhiteSpace(text));
        bool IsWhiteSpace(int index) => IsRebalancedWhiteSpace(data[index]) && (data[index] != '\n' || !preservesLineFeeds);

        if ((offset >= data.Length || !IsWhiteSpace(offset)) && (offset == 0 || !IsWhiteSpace(offset - 1)))
            return;

        var start = offset;
        while (start > 0 && IsWhiteSpace(start - 1))
        {
            start--;
        }

        var end = offset;
        while (end < data.Length && IsWhiteSpace(end))
        {
            end++;
        }

        var followedByText = texts[^1].NextSibling is Text { Length: > 0 } following && !IsRebalancedWhiteSpace(following.Data[0]);
        var noBreakStart = start == 0;
        var noBreakEnd = end == data.Length && !followedByText;
        var previousCharacter = '\0';
        for (var index = start; index < end; index++)
        {
            char character;
            if ((index == start && noBreakStart) || (index == end - 1 && noBreakEnd))
            {
                character = NoBreakSpaceCharacter;
            }
            else if (previousCharacter == ' ')
            {
                character = NoBreakSpaceCharacter;
            }
            else if (previousCharacter == NoBreakSpaceCharacter)
            {
                character = ' ';
            }
            else
            {
                // A sequence of two or more spaces starts with a no-break space
                character = index + 1 < end ? NoBreakSpaceCharacter : ' ';
            }

            previousCharacter = character;
            if (character == data[index])
                continue;

            var textIndex = index;
            foreach (var current in texts)
            {
                if (textIndex < (int)current.Length)
                {
                    ReplaceCharacter(current, textIndex, character);
                    break;
                }

                textIndex -= (int)current.Length;
            }
        }
    }

    /// <summary>
    /// The sequence of white space that contains the character at the offset when it contains a line feed that collapses
    /// (white-space is normal or nowrap): it is rendered as one space, which the deletion of one of its characters removes
    /// whole, as in the browsers. When white-space is pre-line, the line feed is preserved but the spaces and tabs around it
    /// collapse: the deletion of the line feed or of one of these spaces removes the line feed with the spaces around it.
    /// </summary>
    private (int Start, int End)? GetCollapsibleWhiteSpaceRunWithLineFeed(Text text, int offset)
    {
        var data = text.Data;
        if (offset < 0 || offset >= data.Length || data[offset] is not (' ' or '\t' or '\n'))
            return null;

        var whiteSpace = GetWhiteSpace(text);
        if (whiteSpace is "pre-line")
            return GetCollapsibleWhiteSpaceAroundPreservedLineFeed(data, offset);

        if (whiteSpace is not ("normal" or "nowrap"))
            return null;

        var start = offset;
        while (start > 0 && data[start - 1] is ' ' or '\t' or '\n')
        {
            start--;
        }

        var end = offset + 1;
        while (end < data.Length && data[end] is ' ' or '\t' or '\n')
        {
            end++;
        }

        return data.AsSpan(start, end - start).Contains('\n') ? (start, end) : null;
    }

    /// <summary>
    /// The line feed at the offset, or next to the spaces and tabs that contain the offset, with the spaces and tabs around
    /// it (white-space is pre-line); <see langword="null"/> when the character is a space that is not next to a line feed.
    /// </summary>
    private static (int Start, int End)? GetCollapsibleWhiteSpaceAroundPreservedLineFeed(string data, int offset)
    {
        var lineFeed = -1;
        if (data[offset] is '\n')
        {
            lineFeed = offset;
        }
        else
        {
            var before = offset;
            while (before > 0 && data[before - 1] is ' ' or '\t')
            {
                before--;
            }

            var after = offset + 1;
            while (after < data.Length && data[after] is ' ' or '\t')
            {
                after++;
            }

            if (before > 0 && data[before - 1] is '\n')
            {
                lineFeed = before - 1;
            }
            else if (after < data.Length && data[after] is '\n')
            {
                lineFeed = after;
            }
        }

        if (lineFeed < 0)
            return null;

        var start = lineFeed;
        while (start > 0 && data[start - 1] is ' ' or '\t')
        {
            start--;
        }

        var end = lineFeed + 1;
        while (end < data.Length && data[end] is ' ' or '\t')
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>Replaces a character of a Text node without moving the boundary points that are after it.</summary>
    private static void ReplaceCharacter(Text text, int offset, char character)
    {
        text.InsertData((uint)offset, character.ToString());
        text.DeleteData((uint)offset + 1, 1);
    }

    /// <summary>
    /// The first item of the line of a point after it (or before it), in the block that contains it (see
    /// <see cref="FindLineItem"/>): a line feed that white-space preserves is a line break.
    /// </summary>
    private LineItem FindLineItemAt(Node node, int offset, bool forward)
    {
        if (node is Text text)
        {
            var data = text.Data.AsSpan();
            offset = Math.Clamp(offset, 0, data.Length);
            var whiteSpace = GetWhiteSpace(text);
            var preservesSpaces = PreservesWhiteSpace(text.ParentNode);
            for (var index = forward ? offset : offset - 1; index >= 0 && index < data.Length; index += forward ? 1 : -1)
            {
                if (data[index] == '\n' && PreservesLineFeeds(whiteSpace))
                    return LineItem.LineBreak;

                if (preservesSpaces || data[index] is not (' ' or '\t' or '\n' or '\r'))
                    return LineItem.Content;
            }

            return FindLineItem(text, forward);
        }

        if ((forward ? ChildAt(node, offset) : ChildAt(node, offset - 1)) is not { } current)
            return node is Element parent && IsDisplayInline(parent) ? FindLineItem(node, forward) : LineItem.Boundary;

        // The first item of the subtree of the child, else of the nodes that follow it
        while (true)
        {
            if (current is Text)
            {
                var item = FindLineItemAt(current, forward ? 0 : NodeLength(current), forward);
                return item;
            }

            if (current is not Element element)
                break;

            var style = GetStyle(element);
            var display = style.Layout.Display;
            if (display.IsNone)
                break;

            if (element.IsHtml(TagId.Br))
                return LineItem.LineBreak;

            if (!display.IsInlineLevel)
                return LineItem.Boundary;

            if (!display.IsInlineBox || Css.Layout.BoxGeneration.IsReplaced(element, style))
                return LineItem.Content;

            if ((forward ? element.FirstChild : element.LastChild) is not { } child)
                break;

            current = child;
        }

        return FindLineItem(current, forward);
    }

    /// <summary>
    /// Whether the node is an element that is not editable in an editable parent (<c>contenteditable=false</c>): as in the
    /// browsers, it is deleted as a whole with the contents of the selection that contains it.
    /// </summary>
    private static bool IsNonEditableUnit(Node? node)
        => node is Element && !IsEditable(node) && !IsEditingHost(node) && IsEditableOrEditingHost(node.ParentNode);

    /// <summary>A block that is not editable in an editable parent or editing host (e.g. <c>&lt;ul contenteditable=false&gt;</c>).</summary>
    private bool IsNonEditableBlockUnit(Node node) => IsNonEditableUnit(node) && !IsInlineNode(node);

    /// <summary>
    /// Whether the node is an element that the deletion commands delete as a whole, as an img: as in the browsers, the
    /// replaced elements and the form controls (their contents are not editable text), and the elements that are not editable.
    /// An object element without a data attribute represents its fallback content, which is editable text.
    /// </summary>
    private bool IsAtomicElement(Node? node)
    {
        if (node is not Element element)
            return false;

        if (IsNonEditableUnit(element))
            return true;

        if (!element.IsHtmlNamespace)
            return element.ParentNode is Element { IsHtmlNamespace: true };

        if (element.TagId is TagId.Object)
            return !string.IsNullOrEmpty(element.GetAttributeValue("data"));

        return element.TagId is TagId.Img or TagId.Iframe or TagId.Embed or TagId.Video or TagId.Audio or TagId.Canvas or TagId.Meter or TagId.Progress
            or TagId.Input or TagId.Textarea or TagId.Select or TagId.Button;
    }

    /// <summary>
    /// As in the browsers, a deletion in an editing host whose only content is a single empty line in blocks (e.g.
    /// <c>&lt;div&gt;&lt;div&gt;{}&lt;br&gt;&lt;/div&gt;&lt;/div&gt;</c>), where the caret has no other position, removes
    /// the blocks: the editing host gets a line break when it is a block. Returns false when the editing host has another
    /// content (see <see cref="GetOnlyEmptyLineBlock"/>).
    /// </summary>
    private bool EmptyEditingHostWithoutVisibleContent(Element host)
    {
        if (GetOnlyEmptyLineBlock(host) is null)
            return false;

        while (host.FirstChild is { } child)
        {
            host.RemoveChild(child);
        }

        if (!IsDisplayInline(host))
        {
            host.AppendChild(CreateElement("br"));
        }

        CollapseTo(host, 0);
        return true;
    }

    /// <summary>
    /// The block of the single empty line that is the only content of an editing host (see
    /// <see cref="EmptyEditingHostWithoutVisibleContent"/>), or null when the editing host has visible content, several
    /// lines, a list (whose items the deletion commands outdent or join), no block, or when it is the document element.
    /// </summary>
    private Element? GetOnlyEmptyLineBlock(Element host)
    {
        if (ReferenceEquals(host, _document.DocumentElement))
            return null;

        Element? line = null;
        foreach (var descendant in GetDescendants(host))
        {
            switch (descendant)
            {
                case Text when IsVisible(descendant):
                    return null;

                case Element element when !IsEditable(element) || IsAtomicElement(element) || IsHtmlElement(element, "hr") || IsHtmlElement(element, "table") || IsHtmlElement(element, ListNames) || IsHtmlElement(element, ListItemNames):
                    return null;

                case Element element when IsHtmlElement(element, "br"):
                    if (!IsCollapsedBlockProp(element) && !IsExtraneousLineBreak(element))
                        return null;

                    break;

                case Element element when IsBlockNode(element) && !HasBlockChild(element):
                    if (line is not null)
                        return null;

                    line = element;
                    break;

                case Text or Element:
                    break;

                default:
                    if (!IsInvisible(descendant))
                        return null;

                    break;
            }
        }

        return line;

        bool HasBlockChild(Element element)
        {
            for (var child = element.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is Element && IsBlockNode(child))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Deletes the contents of the active range (e.g. the cut action).</summary>
    public void DeleteActiveRange() => DeleteSelection();

    /// <summary>The computed value of white-space of the element of a node (<c>normal</c>, <c>nowrap</c>, <c>pre</c>, <c>pre-wrap</c>, <c>pre-line</c>, <c>break-spaces</c>).</summary>
    private string GetWhiteSpace(Node? node)
    {
        if ((node as Element ?? node?.ParentNode as Element) is not { } element)
            return "normal";

        var layout = GetStyle(element).Layout;
        var nowrap = layout.TextWrapMode == Css.Layout.TextWrapMode.NoWrap;
        return layout.WhiteSpaceCollapse switch
        {
            Css.Layout.WhiteSpaceCollapse.Preserve => nowrap ? "pre" : "pre-wrap",
            Css.Layout.WhiteSpaceCollapse.PreserveBreaks => nowrap ? "preserve-breaks nowrap" : "pre-line",
            Css.Layout.WhiteSpaceCollapse.BreakSpaces => nowrap ? "break-spaces nowrap" : "break-spaces",
            Css.Layout.WhiteSpaceCollapse.PreserveSpaces => nowrap ? "preserve-spaces nowrap" : "preserve-spaces",
            _ => nowrap ? "nowrap" : "normal",
        };
    }

    private static bool PreservesLineFeeds(string whiteSpace) => whiteSpace is "pre" or "pre-wrap" or "pre-line" or "break-spaces";

    /// <summary>
    /// Whether the merge of two blocks only moves the first line of the contents of the end block, from one of its children
    /// (see <see cref="ExtractFirstLine"/>): the white-space of the end block, or of an element of its first line, differs
    /// from the one of the start block, or the br element that ends the first line is in an inline element.
    /// </summary>
    private bool MovesFirstLineOnly(Node startBlock, Node endBlock, int startIndex)
    {
        var whiteSpace = GetWhiteSpace(startBlock);
        if (GetWhiteSpace(endBlock) != whiteSpace)
            return true;

        for (var child = ChildAt(endBlock, startIndex); child is not null && !IsHtmlElement(child, "br") && !IsBlockNode(child); child = child.NextSibling)
        {
            if (child is Element && GetWhiteSpace(child) != whiteSpace)
                return true;

            // The line ends in an inline element, and other lines follow it (the specification moves the element with the
            // lines that follow the br element; as in the browsers, the element is split)
            if (child is Element && !IsAtomicElement(child) && AnyDescendant(child, descendant => IsHtmlElement(descendant, "br") && FindLineItem(descendant, forward: true) != LineItem.Boundary))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the first line of a block, from one of its children: its inline contents until its first line break (a br
    /// element, or a line feed that white-space preserves) or its first block child. The line break is removed. The nodes
    /// of the line keep their white-space when it differs from the one of the block they are moved to: the Text nodes are
    /// wrapped in a span element with the white-space property, and the property is set on the elements. As in the
    /// browsers, the white-space that a pre element gives is not kept, unless it is the one of an author style (the
    /// browsers keep the styles of the author, not the ones of the user agent style sheet).
    /// </summary>
    private List<Node> ExtractFirstLine(Node block, int startIndex, Node destinationBlock)
    {
        var whiteSpace = GetWhiteSpace(block);
        var destinationWhiteSpace = GetWhiteSpace(destinationBlock);
        var keepsWhiteSpace = !IsHtmlElement(destinationBlock, "pre") && (!IsHtmlElement(block, "pre") || HasAuthorWhiteSpace((Element)block));
        Node endNode = block;
        var endOffset = block.ChildCount;
        Node? lineBreak = null;
        Text? lineFeedText = null;
        var stop = NextNodeDescendants(block);
        for (var current = ChildAt(block, startIndex); current is not null && !ReferenceEquals(current, stop);)
        {
            if (current is Text text)
            {
                if (text.Data.IndexOf('\n', StringComparison.Ordinal) is >= 0 and var lineFeed && PreservesLineFeeds(GetWhiteSpace(text)))
                {
                    (endNode, endOffset, lineFeedText) = (text, lineFeed, text);
                    break;
                }
            }
            else if (current is Element element)
            {
                if (element.IsHtml(TagId.Br))
                {
                    (endNode, endOffset, lineBreak) = (element.ParentNode!, NodeIndex(element), element);
                    break;
                }

                if (IsBlockNode(element))
                {
                    (endNode, endOffset) = (element.ParentNode!, NodeIndex(element));
                    break;
                }

                if (IsDisplayNone(element) || IsAtomicElement(element))
                {
                    current = NextNodeDescendants(current);
                    continue;
                }
            }

            current = NextNode(current);
        }

        // The white-space of the nodes of the line, before they are moved
        var whiteSpaces = new List<string>();
        for (var child = ChildAt(block, startIndex); child is not null; child = child.NextSibling)
        {
            whiteSpaces.Add(child is Element ? GetWhiteSpace(child) : whiteSpace);
        }

        var lineRange = _document.CreateRange();
        lineRange.SetStart(block, (uint)Math.Min(startIndex, block.ChildCount));
        lineRange.SetEnd(endNode, (uint)endOffset);
        var fragment = lineRange.ExtractContents();

        // The line break is removed, with the inline elements that it leaves empty
        Node? removed = lineBreak;
        if (lineFeedText is not null)
        {
            lineFeedText.DeleteData(0, 1);
            removed = lineFeedText.Length == 0 ? lineFeedText : null;
        }

        while (removed is not null && !ReferenceEquals(removed, block) && removed.ParentNode is { } parent)
        {
            parent.RemoveChild(removed);
            removed = !ReferenceEquals(parent, block) && !parent.HasChildNodes() && IsInlineNode(parent) ? parent : null;
        }

        var nodes = new List<Node>();
        var index = 0;
        foreach (var node in GetChildren(fragment))
        {
            var nodeWhiteSpace = index < whiteSpaces.Count ? whiteSpaces[index] : whiteSpace;
            index++;
            if (node is Text { Length: > 0 } && keepsWhiteSpace && nodeWhiteSpace != destinationWhiteSpace)
            {
                var span = CreateElement("span");
                span.GetInlineStyle().SetProperty("white-space", nodeWhiteSpace);
                span.AppendChild(node);
                nodes.Add(span);
                continue;
            }

            if (node is Element element && keepsWhiteSpace && nodeWhiteSpace != destinationWhiteSpace)
            {
                element.GetInlineStyle().SetProperty("white-space", nodeWhiteSpace);
            }

            if (node is not Text { Length: 0 })
            {
                nodes.Add(node);
            }
        }

        return nodes;
    }

    /// <summary>Whether the white-space of an element is declared by the author (a style sheet or the style attribute), not by the user agent style sheet.</summary>
    private bool HasAuthorWhiteSpace(Element element)
        => _engine.GetCascadeOrigin(element, "white-space-collapse") == Css.Cascade.CascadeOrigin.Author || _engine.GetCascadeOrigin(element, "text-wrap-mode") == Css.Cascade.CascadeOrigin.Author;

    /// <summary>Removes a block that has no child, and its ancestors that it leaves without child, up to a node.</summary>
    private static void RemoveEmptyBlocks(Node block, Node? stop)
    {
        while (!block.HasChildNodes() && !ReferenceEquals(block, stop) && IsEditable(block) && block.ParentNode is { } parent)
        {
            parent.RemoveChild(block);
            block = parent;
        }
    }

    /// <summary>
    /// The block of a boundary point for the merge of the blocks of a deletion: its closest inclusive ancestor in the same
    /// editing host that is not an inline node, when it can contain inline contents and is not a table cell.
    /// </summary>
    private Node? FindBlockForMerging(Node node)
    {
        var block = node;
        while (InSameEditingHost(block, block.ParentNode) && IsInlineNode(block))
        {
            block = block.ParentNode!;
        }

        if ((!IsBlockNode(block) && !IsEditingHost(block)) || !IsAllowedChild("span", block) || IsHtmlElement(block, TableCellNames))
            return null;

        return block;
    }

    /// <summary>
    /// Whether the delete command removes a node before the caret because it is an editable node that is not rendered (the
    /// specification removes any invisible node). As in the browsers, the head element of an editable document element is
    /// kept.
    /// </summary>
    private bool IsDeletableInvisibleNode([NotNullWhen(true)] Node? node) => IsEditable(node) && IsInvisible(node) && !IsHtmlElement(node, "head");

    /// <summary>
    /// Whether the characters from <paramref name="start"/> to <paramref name="end"/> of an editable Text node are white
    /// space that collapses (white-space is normal or nowrap) at the start of its editing host (<paramref name="forward"/>
    /// is false: they start the Text node, and no node precedes it in the editing host) or at its end (they end the Text
    /// node, and no node follows it in the editing host).
    /// </summary>
    private bool IsCollapsedWhiteSpaceAtEditingHostEdge(Text text, int start, int end, bool forward)
    {
        if (!IsEditable(text) || GetWhiteSpace(text) is not ("normal" or "nowrap") || text.Data.AsSpan(start, end - start).ContainsAnyExcept(" \t\n\f\r"))
            return false;

        Node? node = text;
        while (node is not null && !IsEditingHost(node))
        {
            if ((forward ? node.NextSibling : node.PreviousSibling) is not null)
                return false;

            node = node.ParentNode;
        }

        return node is not null;
    }

    /// <summary>Whether a node is an editable element that is a block without children (e.g. <c>&lt;div&gt;&lt;/div&gt;</c>): it has no line box.</summary>
    private bool IsEmptyEditableBlock(Node node)
        => node is Element && !node.HasChildNodes() && IsEditable(node) && IsBlockNode(node) && !IsHtmlElement(node, "hr") && !IsHtmlElement(node, "table") && !IsAtomicElement(node);

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-delete-command</summary>
    private bool DeleteAction()
    {
        var range = ActiveRange;
        if (!range.Collapsed)
        {
            DeleteSelectionAndRebalance();
            return true;
        }

        // As in the browsers, a caret after the white space that collapses at the start of the editing host has nothing to
        // delete before it: the white space is kept
        if (range.StartContainer is Text startText && range.StartOffset > 0 && IsCollapsedWhiteSpaceAtEditingHostEdge(startText, 0, (int)range.StartOffset, forward: false))
            return true;

        CanonicalizeWhitespace(range.StartContainer, (int)range.StartOffset, replaceSpaces: false);
        var node = range.StartContainer;
        var offset = (int)range.StartOffset;
        while (true)
        {
            if (offset == 0 && IsDeletableInvisibleNode(node.PreviousSibling))
            {
                RemoveNode(node.PreviousSibling);
            }
            else if (ChildAt(node, offset - 1) is { } invisibleChild && IsDeletableInvisibleNode(invisibleChild))
            {
                node.RemoveChild(invisibleChild);
                offset--;
            }
            else if (((offset == 0 && IsInlineNode(node)) || IsInvisible(node)) && !IsEditingHost(node))
            {
                // As in the browsers, the caret does not leave the editing host (e.g. an editing host that only contains
                // collapsed white space is invisible): there is nothing to delete before its start
                if (node.ParentNode is null)
                    break;

                offset = NodeIndex(node);
                node = node.ParentNode;
            }
            else if (ChildAt(node, offset - 1) is { } link && IsEditable(link) && IsHtmlElement(link, "a"))
            {
                RemovePreservingDescendants(link);
                return true;
            }
            else if (ChildAt(node, offset - 1) is { } child && !IsBlockNode(child) && !IsHtmlElement(child, "br") && !IsAtomicElement(child))
            {
                node = child;
                offset = NodeLength(node);
            }
            else
            {
                break;
            }
        }

        if ((node is Text && offset != 0)
            || (IsBlockNode(node) && ChildAt(node, offset - 1) is { } previous && (IsHtmlElement(previous, "br") || IsHtmlElement(previous, "hr") || IsHtmlElement(previous, "img")))
            || IsAtomicElement(ChildAt(node, offset - 1)))
        {
            // The previous character of the text, with the code unit of its surrogate pair
            var start = offset - 1;
            if (node is Text text && start > 0 && char.IsLowSurrogate(text.Data[start]) && char.IsHighSurrogate(text.Data[start - 1]))
            {
                start--;
            }
            else if (node is Text whiteSpaceText && GetCollapsibleWhiteSpaceRunWithLineFeed(whiteSpaceText, start) is { } previousRun)
            {
                start = previousRun.Start;
            }

            SetRange(node, start, node, offset);
            DeleteSelectionAndRebalance();
            return true;
        }

        // As in the browsers, the line after an inline flex or grid container is joined to the last line of its last item
        if (IsInlineNode(node) && IsFlexOrGridContainer(node) && ChildAt(node, offset - 1) is Element lastItem && IsBlockNode(lastItem) && IsEditable(lastItem))
        {
            SetRange(lastItem, NodeLength(lastItem), range.StartContainer, (int)range.StartOffset);
            DeleteSelectionAndRebalance(forward: false);
            return true;
        }

        if (IsInlineNode(node))
            return true;

        if (IsHtmlElement(node, ListItemNames) && ReferenceEquals(node, node.ParentNode!.FirstChild) && offset == 0)
        {
            var items = new List<Node>();
            for (var ancestor = node.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
            {
                if (IsHtmlElement(ancestor, "li"))
                {
                    items.Insert(0, ancestor);
                }
            }

            foreach (var item in items)
            {
                NormalizeSublists(item);
            }

            var values = RecordValues([node]);
            SplitParent([node]);
            RestoreValues(values);
            if (IsHtmlElement(node, DefinitionItemNames) && HasNoAllowedAncestor(node))
            {
                node = SetTagName((Element)node, _state.DefaultSingleLineContainerName);
            }

            FixDisallowedAncestors(node);

            // As in the browsers, the empty item that became another block gets a line break, so that it keeps a line (the
            // fix of the disallowed ancestors replaces the item by a new element, after which the active range is)
            var block = node.ParentNode is null ? ChildAt(ActiveRange.StartContainer, (int)ActiveRange.StartOffset - 1) : node;
            if (block is Element && !IsHtmlElement(block, ListItemNames) && !block.HasChildNodes() && IsEditable(block) && !IsInlineNode(block))
            {
                block.AppendChild(CreateElement("br"));
                CollapseTo(block, 0);
            }

            // As in the browsers, the item that only contained the list of the item (which the normalization of the
            // sublists moved after it) is removed: the item replaces it
            if (IsHtmlElement(node, "li") && IsHtmlElement(node.PreviousSibling, "li") && IsEditable(node.PreviousSibling) && !node.PreviousSibling.HasChildNodes())
            {
                RemoveNode(node.PreviousSibling);
            }

            return true;
        }

        var startNode = node;
        var startOffset = offset;
        while (true)
        {
            if (startOffset == 0)
            {
                if (startNode.ParentNode is null || IsEditingHost(startNode))
                    break;

                startOffset = NodeIndex(startNode);
                startNode = startNode.ParentNode;
            }
            else if (ChildAt(startNode, startOffset - 1) is { } invisibleChild && IsDeletableInvisibleNode(invisibleChild))
            {
                startNode.RemoveChild(invisibleChild);
                startOffset--;
            }
            else
            {
                break;
            }
        }

        // As in the browsers, the head element, when it is not rendered, is not a line that the block after it can join
        if (ChildAt(startNode, startOffset - 1) is { } previousHead && IsHtmlElement(previousHead, "head") && IsInvisible(previousHead))
            return true;

        if (offset == 0 && HasIndentationAncestor(node) && !JoinsFirstLineOfIndentationElement(node, startNode, startOffset))
        {
            var newRange = _document.CreateRange();
            newRange.SetStart(node, 0);
            newRange = BlockExtend(newRange);
            foreach (var current in GetContainedNodes(newRange, static current => IsEditable(current) && !HasEditableDescendants(current)))
            {
                OutdentNode(current);
            }

            return true;
        }

        // There is nothing to delete before the start of the editing host
        if (startOffset == 0 && IsEditingHost(startNode))
        {
            EmptyEditingHostWithoutVisibleContent((Element)startNode);
            return true;
        }

        if (IsHtmlElement(ChildAt(startNode, startOffset), "table"))
            return true;

        if (IsHtmlElement(ChildAt(startNode, startOffset - 1), "table"))
        {
            SetRange(startNode, startOffset - 1, startNode, startOffset);
            return true;
        }

        var previousChild = ChildAt(startNode, startOffset - 1);
        if (offset == 0 && (IsHtmlElement(previousChild, "hr") || (IsHtmlElement(previousChild, "br") && (IsHtmlElement(previousChild.PreviousSibling, "br") || !IsInlineNode(previousChild.PreviousSibling)))))
        {
            SetRange(startNode, startOffset - 1, startNode, startOffset);
            DeleteSelectionAndRebalance();
            CollapseTo(node, Math.Min(offset, NodeLength(node)));
            RebalanceWhiteSpaceAtCaret(acrossSiblings: false);
            return true;
        }

        // The specification only removes a br element that is the previous sibling of the block. As in the browsers, the
        // empty line that precedes the block is removed when its line break is in inline elements (which are removed
        // with it when it is their only content) or is a line feed that white-space preserves
        if (offset == 0 && IsInlineNode(previousChild) && FindEmptyLineBreakAtTheEndOf(previousChild) is var (lineBreakNode, lineBreakOffset))
        {
            SetRange(lineBreakNode, lineBreakOffset, lineBreakNode, lineBreakOffset + 1);
            DeleteSelectionAndRebalance();
            CollapseTo(node, Math.Min(offset, NodeLength(node)));
            RebalanceWhiteSpaceAtCaret(acrossSiblings: false);
            return true;
        }

        // The specification keeps the line break between the lines of two list items that are joined (it appends br
        // elements to the previous item, and moves the contents of the item after them). As in the browsers (and as the
        // tests expect), the li elements whose contents are inline are joined as the other blocks: the first line of the
        // item continues the last line of the previous item
        var currentChild = ChildAt(startNode, startOffset);
        var joinsLines = IsHtmlElement(currentChild, "li") && IsHtmlElement(currentChild.PreviousSibling, "li") && IsInlineNode(currentChild.FirstChild);
        if (!joinsLines && IsHtmlElement(currentChild, ListItemNames) && IsInlineNode(currentChild.FirstChild) && startOffset != 0)
        {
            var previousItem = ChildAt(startNode, startOffset - 1)!;
            if (IsInlineNode(previousItem.LastChild) && !IsHtmlElement(previousItem.LastChild, "br"))
            {
                previousItem.AppendChild(CreateElement("br"));
            }

            if (IsInlineNode(previousItem.LastChild))
            {
                previousItem.AppendChild(CreateElement("br"));
            }
        }

        if (!joinsLines && IsHtmlElement(currentChild, ListItemNames) && IsHtmlElement(currentChild.PreviousSibling, ListItemNames))
        {
            // The active range is restored after the deletion, which merges the two items
            var originalRange = ActiveRange.CloneRange();
            _extraRanges.Add(originalRange);
            startNode = ChildAt(startNode, startOffset - 1)!;
            startOffset = NodeLength(startNode);
            node = startNode.NextSibling!;
            SetRange(startNode, startOffset, node, 0);
            DeleteSelectionAndRebalance();
            SetRange(originalRange.StartContainer, (int)originalRange.StartOffset, originalRange.EndContainer, (int)originalRange.EndOffset);
            _extraRanges.Remove(originalRange);
            return true;
        }

        // As in the browsers, an empty block that follows a block that is not editable is removed (its line cannot be
        // joined to the contents of the block before it)
        if (offset == 0 && IsEditable(node) && IsBlockNode(node) && ReferenceEquals(node, ChildAt(startNode, startOffset))
            && ChildAt(startNode, startOffset - 1) is { } previousBlock && IsNonEditableUnit(previousBlock) && IsBlockNode(previousBlock) && !HasLineContent(node))
        {
            startNode.RemoveChild(node);
            CollapseTo(startNode, startOffset);
            return true;
        }

        var removedNonEditableBlock = false;
        while (true)
        {
            if (ChildAt(startNode, startOffset - 1) is not { } child)
            {
                // After the removal of the blocks that are not editable, the previous line is before the emptied parent
                if (!removedNonEditableBlock || !IsEditable(startNode) || startNode.ParentNode is null)
                    break;

                startOffset = NodeIndex(startNode);
                startNode = startNode.ParentNode;
                continue;
            }

            // As in the browsers, an empty block, which has no line, is removed: the line before it is joined
            if ((IsEditable(child) && IsInvisible(child)) || (IsEmptyEditableBlock(child) && child.PreviousSibling is not null))
            {
                startNode.RemoveChild(child);
                startOffset--;
            }
            else if (IsNonEditableBlockUnit(child))
            {
                // As in the browsers, a block that is not editable (e.g. a list or a list item) is deleted as a whole: the
                // line before it is joined
                if (ReferenceEquals(startNode, node) && offset >= startOffset)
                {
                    offset--;
                }

                startNode.RemoveChild(child);
                startOffset--;
                removedNonEditableBlock = true;
            }
            else
            {
                startNode = child;
                startOffset = NodeLength(startNode);
                removedNonEditableBlock = false;
            }
        }

        SetRange(startNode, startOffset, node, offset);
        DeleteSelectionAndRebalance(forward: false);
        return true;
    }

    /// <summary>
    /// Whether the deletion at the start of a block joins its first line to the line before it instead of outdenting it
    /// (the specification always outdents): as in the browsers, when the block is a simple indentation element (e.g. a
    /// blockquote element without attributes) that is not in another indentation element and that follows a node of its
    /// editing host.
    /// </summary>
    private static bool JoinsFirstLineOfIndentationElement(Node node, Node startNode, int startOffset)
        => IsEditable(node) && IsSimpleIndentationElement(node) && !HasIndentationAncestor(node.ParentNode!)
            && ChildAt(startNode, startOffset - 1) is { } previous && InSameEditingHost(previous, node);

    /// <summary>
    /// The line break that ends the inline contents of an inline node (the point before a br element, or before a line feed
    /// that white-space preserves), when it is editable and its line has no other content (an empty line); else null. The
    /// nodes that are not rendered and the white space that collapses are skipped.
    /// </summary>
    private (Node Node, int Offset)? FindEmptyLineBreakAtTheEndOf(Node node)
    {
        var current = node;
        while (true)
        {
            if (IsHtmlElement(current, "br"))
                return IsEditable(current) && FindLineItem(current, forward: false) != LineItem.Content ? (current.ParentNode!, NodeIndex(current)) : null;

            if (current is Text { Length: > 0 } text && text.Data[^1] == '\n' && PreservesLineFeeds(GetWhiteSpace(text)))
                return IsEditable(text) && FindLineItemAt(text, (int)text.Length - 1, forward: false) != LineItem.Content ? (text, (int)text.Length - 1) : null;

            if (current is Element element && IsDisplayInline(element) && !IsAtomicElement(element) && element.LastChild is { } last)
            {
                current = last;
                continue;
            }

            // A node without content is skipped: the node before it ends the line
            if (!IsInvisible(current) || ReferenceEquals(current, node))
                return null;

            while (current.PreviousSibling is null)
            {
                current = current.ParentNode!;
                if (ReferenceEquals(current, node))
                    return null;
            }

            current = current.PreviousSibling;
        }
    }

    /// <summary>
    /// The line break at a point (the point before a br element, or before a line feed that white-space preserves) and the
    /// block that follows it, when the line break is editable, its line has no other content (an empty line) and a block
    /// that can contain the caret starts after it; else null.
    /// </summary>
    private (Node Node, int Offset, Node Block)? FindEmptyLineBeforeBlock(Node node, int offset)
    {
        Node current;
        if (node is Text text)
        {
            if (offset != (int)text.Length - 1 || text.Data[offset] != '\n' || !PreservesLineFeeds(GetWhiteSpace(text)) || !IsEditable(text))
                return null;

            current = text;
        }
        else if (ChildAt(node, offset) is { } child && IsHtmlElement(child, "br") && IsEditable(child))
        {
            current = child;
        }
        else
        {
            return null;
        }

        if (FindLineItemAt(node, offset, forward: false) == LineItem.Content)
            return null;

        while (true)
        {
            while (current.NextSibling is null)
            {
                if (current.ParentNode is not Element parent || !IsDisplayInline(parent))
                    return null;

                current = parent;
            }

            current = current.NextSibling;
            if (IsBlockNode(current))
                return IsEditable(current) && !IsHtmlElement(current, "hr") && !IsHtmlElement(current, "table") ? (node, offset, current) : null;

            if (!IsInvisible(current))
                return null;
        }
    }

    /// <summary>
    /// The node when it is an editable block (not a list item nor a table cell) whose children are invisible or collapsed
    /// block props, in an editable block parent (not the editing host, where the content that follows the block is moved
    /// into it, as the tests expect: <c>&lt;p&gt;{}&lt;br&gt;&lt;/p&gt;foo</c> gives <c>&lt;p&gt;foo&lt;/p&gt;</c>), and
    /// followed by visible inline content of this parent; otherwise null.
    /// </summary>
    private Element? GetEmptyChildBlockBeforeInlineContent(Node node)
    {
        if (node is not Element block || !IsEditable(block) || IsInlineNode(block) || IsEditingHost(block) || !IsEditable(block.ParentNode) || IsInlineNode(block.ParentNode)
            || IsHtmlElement(block, ListItemNames) || IsHtmlElement(block, TableSectionNames) || IsHtmlElement(block, "hr") || IsHtmlElement(block, "table") || IsAtomicElement(block))
            return null;

        if (!GetChildren(block).TrueForAll(child => IsInvisible(child) || IsCollapsedBlockProp(child)))
            return null;

        return GetNextVisibleSibling(block) is { } next && IsInlineNode(next) && IsEditable(next) ? block : null;
    }

    private bool IsFlexOrGridContainer(Node node) => node is Element element && GetDisplay(element) is var display && (display.IsFlexContainer || display.IsGridContainer);

    /// <summary>Whether the node has a descendant that is rendered content (text that is not only white space, or an element deleted as a whole).</summary>
    private bool HasRenderedContent(Node node)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if ((child is Text && !IsWhitespaceNode(child)) || IsAtomicElement(child) || HasRenderedContent(child))
                return true;
        }

        return false;
    }

    private Node? GetNextVisibleSibling(Node node)
    {
        var next = node.NextSibling;
        while (next is not null && IsInvisible(next))
        {
            next = next.NextSibling;
        }

        return next;
    }

    private static bool HasIndentationAncestor(Node node)
    {
        for (Node? ancestor = node; ancestor is not null; ancestor = ancestor.ParentNode)
        {
            if (IsEditable(ancestor) && InSameEditingHost(ancestor, node) && IsIndentationElement(ancestor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the boundary point is followed by at least one node of its editing host, all of which are invisible (the
    /// text of a Text node that contains the point is visible content after it).
    /// </summary>
    private bool HasOnlyInvisibleNodesAfter(Node node, int offset)
    {
        if (GetEditingHostOf(node) is not { } host || (node is CharacterData && offset < NodeLength(node)))
            return false;

        var current = ChildAt(node, offset) ?? NextNodeDescendants(node);
        if (current is null || !host.IsInclusiveAncestorOf(current))
            return false;

        for (; current is not null && host.IsInclusiveAncestorOf(current); current = NextNode(current))
        {
            if (!IsEditable(current) || IsVisible(current))
                return false;
        }

        return true;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-forwarddelete-command</summary>
    private bool ForwardDeleteAction()
    {
        var range = ActiveRange;
        if (!range.Collapsed)
        {
            DeleteSelectionAndRebalance();
            return true;
        }

        // As in the browsers, a caret before the white space that collapses at the end of the editing host has nothing to
        // delete after it: the white space is kept
        if (range.StartContainer is Text startText && range.StartOffset < startText.Length && IsCollapsedWhiteSpaceAtEditingHostEdge(startText, (int)range.StartOffset, (int)startText.Length, forward: true))
            return true;

        // As in the browsers, a caret at the end of the content of the editing host has nothing to delete after it: the
        // invisible nodes that follow it (e.g. the br that ends the last block, <p>abc[]<br></p>) are kept
        if (HasOnlyInvisibleNodesAfter(range.StartContainer, (int)range.StartOffset))
            return true;

        CanonicalizeWhitespace(range.StartContainer, (int)range.StartOffset, replaceSpaces: false);
        var node = range.StartContainer;
        var offset = (int)range.StartOffset;
        while (true)
        {
            if (offset == NodeLength(node) && IsEditable(node.NextSibling) && IsInvisible(node.NextSibling))
            {
                RemoveNode(node.NextSibling);
            }
            else if (ChildAt(node, offset) is { } invisibleChild && IsEditable(invisibleChild) && IsInvisible(invisibleChild))
            {
                node.RemoveChild(invisibleChild);
            }
            else if (ChildAt(node, offset) is Text whiteSpaceText && IsEditable(whiteSpaceText) && IsWhitespaceNode(whiteSpaceText) && !PreservesWhiteSpace(whiteSpaceText.ParentNode)
                && FindLineItem(whiteSpaceText, forward: true) == LineItem.Boundary)
            {
                // As in the browsers, the white space that collapses at the end of a block is not rendered: it is removed,
                // and the next line is joined (<p>abc{} </p><p>def</p>)
                node.RemoveChild(whiteSpaceText);
            }
            else if ((offset == NodeLength(node) && IsInlineNode(node)) || IsInvisible(node))
            {
                if (node.ParentNode is null)
                    break;

                offset = 1 + NodeIndex(node);
                node = node.ParentNode;
            }
            else if (ChildAt(node, offset) is { } child && !IsBlockNode(child) && !IsHtmlElement(child, "br") && !IsAtomicElement(child) && !IsCollapsedBlockProp(child))
            {
                node = child;
                offset = 0;
            }
            else
            {
                break;
            }
        }

        // The specification joins the first line of the block that follows an empty line to this line. As in the browsers
        // (and as the tests expect), the empty line is removed: the caret is at the start of the block
        if (FindEmptyLineBeforeBlock(node, offset) is var (lineBreakNode, lineBreakOffset, nextBlock))
        {
            SetRange(lineBreakNode, lineBreakOffset, lineBreakNode, lineBreakOffset + 1);
            DeleteSelectionAndRebalance();
            CollapseTo(nextBlock, 0);
            RebalanceWhiteSpaceAtCaret(acrossSiblings: false);
            return true;
        }

        // The specification moves the inline content that follows an empty child block into it. As in the browsers, the
        // empty block is removed from its parent block: the caret is before the content (<div><div>{}<br></div>abc</div>
        // gives <div>abc</div>)
        if (GetEmptyChildBlockBeforeInlineContent(node) is { } emptyBlock)
        {
            var parent = emptyBlock.ParentNode!;
            var index = NodeIndex(emptyBlock);
            RemoveNode(emptyBlock);
            CollapseTo(parent, index);
            RebalanceWhiteSpaceAtCaret(acrossSiblings: false);
            return true;
        }

        // As in the browsers, an empty flex or grid container that is followed by a block is removed (its items are not a
        // line the next block can be joined to): the caret is at the start of the block
        if (GetFlexOrGridItem(node) is { ParentNode: Element container } && IsEditable(container) && !HasRenderedContent(container)
            && GetNextVisibleSibling(container) is { } blockAfterContainer && IsBlockNode(blockAfterContainer) && IsEditable(blockAfterContainer))
        {
            RemoveNode(container);
            CollapseTo(blockAfterContainer, 0);
            return true;
        }

        if (node is Text text && offset != NodeLength(node))
        {
            // The next character, with the code unit of its surrogate pair and the combining marks that follow it
            var data = text.Data;
            var endOffset = offset + 1;
            if (char.IsHighSurrogate(data[offset]) && endOffset < data.Length && char.IsLowSurrogate(data[endOffset]))
            {
                endOffset++;
            }

            while (endOffset != data.Length && IsCombiningMark(data[endOffset]))
            {
                endOffset++;
            }

            if (GetCollapsibleWhiteSpaceRunWithLineFeed(text, offset) is { } nextRun)
            {
                endOffset = nextRun.End;
            }

            SetRange(node, offset, node, endOffset);
            DeleteSelectionAndRebalance();
            return true;
        }

        // An element that is deleted as a whole at the end of an inline element (foo<span>bar[]<span contenteditable=false>
        // baz</span></span>) is deleted, as in the browsers
        if (IsInlineNode(node) && ChildAt(node, offset) is { } atomicChild && IsAtomicElement(atomicChild) && IsEditableOrEditingHost(node))
        {
            SetRange(node, offset, node, offset + 1);
            DeleteSelectionAndRebalance();
            return true;
        }

        // As in the browsers, the first line of the first item of an inline flex or grid container is joined to the line
        // before the container
        if (IsInlineNode(node) && IsFlexOrGridContainer(node) && ChildAt(node, offset) is Element firstItem && IsBlockNode(firstItem) && IsEditable(firstItem))
        {
            SetRange(range.StartContainer, (int)range.StartOffset, firstItem, 0);
            DeleteSelectionAndRebalance();
            return true;
        }

        if (IsInlineNode(node))
            return true;

        // There is nothing to delete after the caret when the editing host has no visible content
        if (GetEditingHostOf(node) is Element host && EmptyEditingHostWithoutVisibleContent(host))
            return true;

        var nextChild = ChildAt(node, offset);
        if ((IsHtmlElement(nextChild, "br") || IsHtmlElement(nextChild, "hr") || IsAtomicElement(nextChild)) && !IsCollapsedBlockProp(nextChild))
        {
            SetRange(node, offset, node, offset + 1);
            DeleteSelectionAndRebalance();
            return true;
        }

        var endNode = node;
        var end = offset;
        if (IsCollapsedBlockProp(ChildAt(endNode, end)))
        {
            end++;
        }

        while (true)
        {
            if (end == NodeLength(endNode))
            {
                if (endNode.ParentNode is null)
                    break;

                end = 1 + NodeIndex(endNode);
                endNode = endNode.ParentNode;
            }
            else if (ChildAt(endNode, end) is { } invisibleChild && IsEditable(invisibleChild) && IsInvisible(invisibleChild))
            {
                endNode.RemoveChild(invisibleChild);
            }
            else
            {
                break;
            }
        }

        if (IsHtmlElement(ChildAt(endNode, end - 1), "table"))
            return true;

        if (IsHtmlElement(ChildAt(endNode, end), "table"))
        {
            SetRange(endNode, end, endNode, end + 1);
            return true;
        }

        if (offset == NodeLength(node) && (IsHtmlElement(ChildAt(endNode, end), "br") || IsHtmlElement(ChildAt(endNode, end), "hr")))
        {
            SetRange(endNode, end, endNode, end + 1);
            DeleteSelectionAndRebalance();
            CollapseTo(node, Math.Min(offset, NodeLength(node)));
            RebalanceWhiteSpaceAtCaret(acrossSiblings: false);
            return true;
        }

        var removedNonEditableBlock = false;
        while (true)
        {
            if (ChildAt(endNode, end) is not { } child)
            {
                // After the removal of the blocks that are not editable, the next line is after the emptied parent
                if (!removedNonEditableBlock || !IsEditable(endNode) || endNode.ParentNode is null)
                    break;

                end = 1 + NodeIndex(endNode);
                endNode = endNode.ParentNode;
                continue;
            }

            // As in the browsers, an empty block, which has no line, is removed: the line after it is joined
            if ((IsEditable(child) && IsInvisible(child)) || (IsEmptyEditableBlock(child) && child.NextSibling is not null))
            {
                endNode.RemoveChild(child);
            }
            else if (IsNonEditableBlockUnit(child))
            {
                // As in the browsers, a block that is not editable (e.g. a list or a list item) is deleted as a whole: the
                // line after it is joined
                endNode.RemoveChild(child);
                removedNonEditableBlock = true;
            }
            else
            {
                endNode = child;
                end = 0;
                removedNonEditableBlock = false;
            }
        }

SetRange(node, offset, endNode, end);
        DeleteSelectionAndRebalance();
        return true;
    }
}
