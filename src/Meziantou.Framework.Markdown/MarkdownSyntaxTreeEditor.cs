using System.Text;

namespace Meziantou.Framework.Markdown;

/// <summary>Changes several blocks of a <see cref="MarkdownSyntaxTree"/>, and parses the new text once.</summary>
/// <remarks>
/// <para>
/// Each method records a change to a block of <see cref="OriginalTree"/>, which does what the method of the same name of
/// <see cref="MarkdownSyntaxTree"/> does. <see cref="GetChangedTree"/> writes all the changes into the text, and parses it once: the
/// time to change every block of a document is proportional to the length of the document, while a loop of
/// <see cref="MarkdownSyntaxTree"/> edits parses the whole text for each of them.
/// </para>
/// <para>
/// The blocks are the ones of <see cref="OriginalTree"/>, as they were before the changes. Text inserted before or after a block, a
/// replaced block and a removed block cannot overlap: changing a block and a block it contains throws, except that removing a block
/// removes the blocks it contains.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownSyntaxTree_HeadingIdentifiers" ref:dedent="true">
/// var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.AutoIdentifiers };
/// var tree = MarkdownSyntaxTree.ParseText("# Introduction\n\nGetting started {.lead}\n===\n", options);
///
/// // Write the identifier of every heading as its id, so that the links to it keep working when its text changes. The editor
/// // parses the text once, after all the changes: the headings are the ones of the original tree.
/// var editor = new MarkdownSyntaxTreeEditor(tree);
/// foreach (var heading in tree.GetRoot().DescendantNodes().OfType&lt;MarkdownHeadingSyntax&gt;())
/// {
///     editor.SetGenericAttributes(heading, heading.GetGenericAttributes() with { Id = heading.Identifier });
/// }
///
/// tree = editor.GetChangedTree();
///
/// Assert.Equal("# Introduction {#introduction}\n\nGetting started {#getting-started .lead}\n===\n", tree.GetRoot().ToFullString());
/// </code>
/// </example>
public sealed class MarkdownSyntaxTreeEditor
{
    private const int CodeIndent = 4;

    // The order of the changes at the same position: the text inserted after a block, then the text inserted before the next
    // block, then the text that replaces a range
    private const int InsertAfterOrder = 0;
    private const int InsertBeforeOrder = 1;
    private const int ReplaceOrder = 2;

    private readonly string _text;
    private readonly string _newLine;
    private readonly List<Change> _changes = [];
    private readonly List<MarkdownBlockSyntax> _removedBlocks = [];
    private readonly List<AttributesEdit> _attributesEdits = [];
    private readonly Dictionary<MarkdownBlockSyntax, int> _attributesEditIndexes = [];
    private readonly Dictionary<MarkdownListItemSyntax, int> _itemWidths = [];
    private Dictionary<MarkdownBlockSyntax, List<MarkdownGenericAttributesSyntax>>? _attributesTargets;

    /// <summary>Initializes a new instance of the <see cref="MarkdownSyntaxTreeEditor"/> class, without changes.</summary>
    /// <param name="tree">The tree whose blocks are changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public MarkdownSyntaxTreeEditor(MarkdownSyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        OriginalTree = tree;
        _text = tree.GetText().Text;
        _newLine = GetNewLine(_text);
    }

    /// <summary>Gets the tree whose blocks are changed, which the changes do not modify.</summary>
    public MarkdownSyntaxTree OriginalTree { get; }

    /// <summary>Inserts blocks before <paramref name="block"/>, in its container.</summary>
    /// <param name="block">The block of <see cref="OriginalTree"/> to insert before.</param>
    /// <param name="markdown">The blocks to insert, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <remarks>See <see cref="MarkdownSyntaxTree.InsertBlocksBefore(MarkdownBlockSyntax, string)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of <see cref="OriginalTree"/>.</exception>
    public void InsertBlocksBefore(MarkdownBlockSyntax block, string markdown)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(markdown);
        EnsureBlockOfTree(block);

        // The first line goes where the block starts, after the markers of its line, and the block moves to a line of its own
        var (start, end, lead, afterLine) = GetEditStart(block);
        var prefix = GetContinuationPrefix(block);
        var lines = SplitLines(markdown);
        var inserted = new StringBuilder(lead);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                AppendPrefixed(inserted, prefix, lines[i]);
            }
            else
            {
                inserted.Append(lines[i]);
            }

            inserted.Append(_newLine);
        }

        // After the line of the kind of an alert, the line ending of this line separates the lines from the block
        if (afterLine)
        {
            inserted.Length -= _newLine.Length;
        }
        else
        {
            inserted.Append(prefix);
        }

        _changes.Add(new Change(start, end, inserted.ToString(), InsertBeforeOrder));
    }

    /// <summary>Inserts blocks after <paramref name="block"/>, in its container.</summary>
    /// <param name="block">The block of <see cref="OriginalTree"/> to insert after.</param>
    /// <param name="markdown">The blocks to insert, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <remarks>See <see cref="MarkdownSyntaxTree.InsertBlocksAfter(MarkdownBlockSyntax, string)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of <see cref="OriginalTree"/>.</exception>
    public void InsertBlocksAfter(MarkdownBlockSyntax block, string markdown)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(markdown);
        EnsureBlockOfTree(block);

        var prefix = GetContinuationPrefix(block);
        var (lineEnd, nextLine) = GetEndOfLastLine(_text, block);
        var inserted = new StringBuilder();
        if (nextLine == lineEnd)
        {
            // The block ends the text without a line ending
            inserted.Append(_newLine);
        }

        // A fenced block that is not closed would read the new lines as its content: the block, or the last block it ends with
        var lastBlocks = new List<MarkdownBlockSyntax>();
        for (var last = block; last is not null; last = last.ChildNodes().OfType<MarkdownBlockSyntax>().LastOrDefault())
        {
            lastBlocks.Add(last);

            // The closing fence of a custom container or of a figure closes the blocks it contains
            if (last is MarkdownCustomContainerSyntax { ClosingFenceToken.RawKind: not 0 } or MarkdownFigureSyntax { ClosingFenceToken.RawKind: not 0 })
                break;
        }

        // The fence of a custom container or of a figure closes the blocks it contains
        var innermost = lastBlocks.FindIndex(last => last is MarkdownCustomContainerSyntax or MarkdownFigureSyntax && GetMissingClosingFence(last) is not null);
        for (var i = innermost >= 0 ? innermost : lastBlocks.Count - 1; i >= 0; i--)
        {
            if (GetMissingClosingFence(lastBlocks[i]) is { } fence)
            {
                inserted.Append(GetContinuationPrefix(lastBlocks[i])).Append(fence).Append(_newLine);
            }
        }

        foreach (var line in SplitLines(markdown))
        {
            AppendPrefixed(inserted, prefix, line);
            inserted.Append(_newLine);
        }

        if (nextLine == lineEnd)
        {
            inserted.Length -= _newLine.Length;
        }

        _changes.Add(new Change(nextLine, nextLine, inserted.ToString(), InsertAfterOrder));
    }

    /// <summary>Replaces <paramref name="block"/> with other blocks, in its container.</summary>
    /// <param name="block">The block of <see cref="OriginalTree"/> to replace.</param>
    /// <param name="markdown">The blocks that replace it, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <remarks>See <see cref="MarkdownSyntaxTree.ReplaceBlock(MarkdownBlockSyntax, string)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of <see cref="OriginalTree"/>.</exception>
    public void ReplaceBlock(MarkdownBlockSyntax block, string markdown)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(markdown);
        EnsureBlockOfTree(block);

        var (start, _, lead, _) = GetEditStart(block);
        var prefix = GetContinuationPrefix(block);
        var (lineEnd, _) = GetEndOfLastLine(_text, block);
        var lines = SplitLines(markdown);
        var replacement = new StringBuilder(lead);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                replacement.Append(_newLine);
                AppendPrefixed(replacement, prefix, lines[i]);
            }
            else
            {
                replacement.Append(lines[i]);
            }
        }

        _changes.Add(new Change(start, lineEnd, replacement.ToString(), ReplaceOrder));
    }

    /// <summary>Removes <paramref name="block"/> and its lines.</summary>
    /// <param name="block">The block of <see cref="OriginalTree"/> to remove.</param>
    /// <remarks>See <see cref="MarkdownSyntaxTree.RemoveBlock(MarkdownBlockSyntax)"/>. Removing blocks that follow each other removes their lines together.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of <see cref="OriginalTree"/>.</exception>
    public void RemoveBlock(MarkdownBlockSyntax block)
    {
        ArgumentNullException.ThrowIfNull(block);
        EnsureBlockOfTree(block);

        _removedBlocks.Add(block);
    }

    /// <summary>Replaces the generic attributes of <paramref name="block"/>.</summary>
    /// <param name="block">The block of <see cref="OriginalTree"/> whose attributes are replaced.</param>
    /// <param name="attributes">The attributes of the block, or <see langword="null"/> to remove them.</param>
    /// <remarks>
    /// See <see cref="MarkdownSyntaxTree.WithGenericAttributes(MarkdownBlockSyntax, MarkdownGenericAttributes?)"/>.
    /// <see cref="GetChangedTree"/> checks that the block has the attributes in the new tree, and throws when it does not.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="block"/> is not a block of <see cref="OriginalTree"/>, or <paramref name="attributes"/> cannot be written, such as
    /// a class with whitespace or an attribute name that starts with a digit.
    /// </exception>
    /// <exception cref="InvalidOperationException">The tree is parsed without <see cref="MarkdownExtensions.GenericAttributes"/>.</exception>
    public void SetGenericAttributes(MarkdownBlockSyntax block, MarkdownGenericAttributes? attributes)
    {
        ArgumentNullException.ThrowIfNull(block);
        EnsureBlockOfTree(block);
        if (!OriginalTree.Options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes))
            throw new InvalidOperationException("The tree is parsed without generic attributes.");

        attributes ??= MarkdownGenericAttributes.Empty;
        var markdown = attributes.IsEmpty ? null : attributes.ToMarkdown(nameof(attributes));

        // The last attributes given to a block replace the others
        if (_attributesEditIndexes.Remove(block, out var existing))
        {
            _attributesEdits[existing] = _attributesEdits[existing] with { Removed = true };
        }

        if (block.GetGenericAttributes().Equals(attributes))
            return;

        // The text of the block, which the attributes are not part of, does not change, but the whitespace around them can, such as
        // the line ending that attributes in a paragraph end with
        var removals = GetAttributesRemovals(block);
        var candidates = new List<Change?>();
        foreach (var insertion in GetAttributesInsertions(block, markdown))
        {
            // The changes of an edit cannot overlap
            if (insertion is not { } change || !Overlaps(removals, change))
            {
                candidates.Add(insertion);
            }
        }

        _attributesEditIndexes.Add(block, _attributesEdits.Count);
        _attributesEdits.Add(new AttributesEdit(block, attributes, removals, candidates, CollapseWhitespace(block.GetPlainText())));
    }

    /// <summary>Gets a tree parsed from the text of <see cref="OriginalTree"/> with the changes.</summary>
    /// <returns>The new tree, or <see cref="OriginalTree"/> when there is no change.</returns>
    /// <exception cref="InvalidOperationException">
    /// Changes overlap, such as the replacements of a block and of a block it contains, or a block cannot have the attributes it is given,
    /// such as an HTML block, a list item or a footnote definition, which Markdig gives no attributes to.
    /// </exception>
    public MarkdownSyntaxTree GetChangedTree()
    {
        var changes = new List<Change>(_changes);
        AddRemovals(changes);
        if (_attributesEditIndexes.Count == 0)
            return changes.Count == 0 ? OriginalTree : Parse(SortWithoutOverlap(changes));

        // The attributes are written at the first place that the block reads them from: when a place is not read as attributes of the
        // block, the next one is tried
        var candidateIndexes = new int[_attributesEdits.Count];
        while (true)
        {
            var all = new List<Change>(changes);
            for (var i = 0; i < _attributesEdits.Count; i++)
            {
                var edit = _attributesEdits[i];
                if (edit.Removed)
                    continue;

                if (candidateIndexes[i] >= edit.Candidates.Count)
                    throw CannotHaveAttributes(edit.Block);

                foreach (var removal in edit.Removals)
                {
                    all.Add(removal with { Owner = i });
                }

                if (edit.Candidates[candidateIndexes[i]] is { } insertion)
                {
                    all.Add(insertion with { Owner = i, IsCandidate = true });
                }
            }

            var sorted = Sort(all, out var ownerOfOverlap);
            if (ownerOfOverlap == -2)
                throw Overlap();

            if (ownerOfOverlap >= 0)
            {
                candidateIndexes[ownerOfOverlap]++;
                continue;
            }

            var tree = Parse(sorted);
            var failed = GetEditsWithoutTheirAttributes(tree, sorted);
            if (failed.Count == 0)
                return tree;

            foreach (var index in failed)
            {
                candidateIndexes[index]++;
            }
        }
    }

    private static InvalidOperationException CannotHaveAttributes(MarkdownBlockSyntax block) => new($"The {block.Kind()} cannot have these generic attributes: the text written for them is not read as attributes of the block.");

    private MarkdownSyntaxTree Parse(List<Change> sorted)
    {
        var builder = new StringBuilder(_text.Length);
        var position = 0;
        foreach (var change in sorted)
        {
            Append(builder, _text.AsSpan(position, change.Start - position));
            Append(builder, change.Text);
            position = change.End;
        }

        Append(builder, _text.AsSpan(position));
        return OriginalTree.Reparse(SourceText.From(builder.ToString()));
    }

    // A \r and a \n that the changes put next to each other would be one line ending: the \r becomes \r\n
    private static void Append(StringBuilder builder, ReadOnlySpan<char> text)
    {
        if (text.Length > 0 && text[0] == '\n' && builder.Length > 0 && builder[^1] == '\r')
        {
            builder.Append('\n');
        }

        builder.Append(text);
    }

    private static List<Change> SortWithoutOverlap(List<Change> changes)
    {
        var sorted = Sort(changes, out var ownerOfOverlap);
        if (ownerOfOverlap != -1)
            throw Overlap();

        return sorted;
    }

    private static InvalidOperationException Overlap() => new("The changes overlap: a block is changed twice, or a block and a block it contains are changed.");

    // The changes in the order of the text. When two changes overlap, the owner is the index of the attributes edit whose place to write
    // the attributes is to be changed, or -2 when they cannot be applied together.
    private static List<Change> Sort(List<Change> changes, out int ownerOfOverlap)
    {
        var sorted = changes.OrderBy(change => change.Start).ThenBy(change => change.Order).ToList();
        ownerOfOverlap = -1;
        Change? last = null;
        foreach (var change in sorted)
        {
            if (last is { } previous && change.Start < previous.End)
            {
                ownerOfOverlap = change.IsCandidate ? change.Owner : previous.IsCandidate ? previous.Owner : -2;
                return sorted;
            }

            if (last is null || change.End >= last.Value.End)
            {
                last = change;
            }
        }

        return sorted;
    }

    // The indexes of the attributes edits whose block does not have the attributes in the new tree, or whose text changed
    private List<int> GetEditsWithoutTheirAttributes(MarkdownSyntaxTree tree, List<Change> sorted)
    {
        var root = tree.GetRoot();
        var blocks = new Dictionary<(int Start, int Kind), MarkdownBlockSyntax>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is MarkdownBlockSyntax block)
            {
                blocks.TryAdd((block.SpanStart, block.RawKind), block);
            }
        }

        var targets = GetAttributesTargets(root);
        var shifts = GetShifts(sorted);
        var failed = new List<int>();
        for (var i = 0; i < _attributesEdits.Count; i++)
        {
            var edit = _attributesEdits[i];
            if (edit.Removed)
                continue;

            var block = edit.Block;
            if (!blocks.TryGetValue((MapPosition(sorted, shifts, block.SpanStart), block.RawKind), out var updated)
                || !MarkdownBlockSyntax.MergeGenericAttributes(targets.GetValueOrDefault(updated) ?? []).Equals(edit.Attributes)
                || (!HasOtherChangeInside(sorted, i, block) && CollapseWhitespace(updated.GetPlainText()) != edit.PlainText))
            {
                failed.Add(i);
            }
        }

        return failed;
    }

    // The position of a character of the original text in the new text: the text inserted where it is goes before it. The changes do
    // not overlap: their ends are in the order of their starts.
    private static int MapPosition(List<Change> sorted, int[] shifts, int position)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sorted[middle].End <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return position + shifts[low];
    }

    // The shift of the positions after each number of changes
    private static int[] GetShifts(List<Change> sorted)
    {
        var shifts = new int[sorted.Count + 1];
        for (var i = 0; i < sorted.Count; i++)
        {
            shifts[i + 1] = shifts[i] + sorted[i].Text.Length - (sorted[i].End - sorted[i].Start);
        }

        return shifts;
    }

    // Whether a change of another edit is in the block, which changes its text
    private static bool HasOtherChangeInside(List<Change> sorted, int owner, MarkdownBlockSyntax block)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sorted[middle].Start <= block.SpanStart)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var i = low; i < sorted.Count && sorted[i].Start < block.Span.End; i++)
        {
            if (sorted[i].Owner != owner)
                return true;
        }

        return false;
    }

    private static Dictionary<MarkdownBlockSyntax, List<MarkdownGenericAttributesSyntax>> GetAttributesTargets(MarkdownDocumentSyntax root)
    {
        var targets = new Dictionary<MarkdownBlockSyntax, List<MarkdownGenericAttributesSyntax>>();
        if (root.HasGenericAttributes)
        {
            _ = MarkdownGenericAttributesSyntax.FindTargets(root, (attributes, target) =>
            {
                if (target is MarkdownBlockSyntax block)
                {
                    if (!targets.TryGetValue(block, out var list))
                    {
                        list = [];
                        targets.Add(block, list);
                    }

                    list.Add(attributes);
                }
            });
        }

        return targets;
    }

    // The removed blocks that follow each other in a container are removed together, so that the lines around them are handled once
    private void AddRemovals(List<Change> changes)
    {
        if (_removedBlocks.Count == 0)
            return;

        var removed = new HashSet<MarkdownBlockSyntax>(_removedBlocks);
        var handled = new HashSet<MarkdownBlockSyntax>();
        var siblingsByParent = new Dictionary<SyntaxNode, List<MarkdownBlockSyntax>>();
        foreach (var block in _removedBlocks.OrderBy(block => block.SpanStart))
        {
            // A block in a removed block is removed with it
            if (handled.Contains(block) || block.Ancestors().Any(ancestor => ancestor is MarkdownBlockSyntax container && removed.Contains(container)))
                continue;

            var parent = block.Parent!;
            if (!siblingsByParent.TryGetValue(parent, out var siblings))
            {
                siblings = [.. parent.ChildNodes().OfType<MarkdownBlockSyntax>()];
                siblingsByParent.Add(parent, siblings);
            }

            var last = siblings.IndexOf(block);
            while (last + 1 < siblings.Count && removed.Contains(siblings[last + 1]))
            {
                last++;
                handled.Add(siblings[last]);
            }

            var lastBlock = siblings[last];
            var next = last + 1 < siblings.Count ? siblings[last + 1] : null;
            AddRemoval(changes, block, lastBlock, next);
        }
    }

    // Removes the lines of the blocks from first to last, which follow each other in their container
    private void AddRemoval(List<Change> changes, MarkdownBlockSyntax first, MarkdownBlockSyntax last, MarkdownBlockSyntax? next)
    {
        changes.Add(GetRemoval(first, last, next, out var nextLine, out var wholeLines));

        // The first line of a paragraph can be indented by 4 columns or more when it continues a link reference definition: without the
        // definition, the indentation would make it an indented code block, and the markers of its containers are needed when it is a
        // lazy continuation line
        if (wholeLines && next is MarkdownParagraphSyntax && GetLineStart(_text, next.SpanStart) == nextLine && GetEditStart(next) is { AfterLine: false } nextStart && (nextStart.Start < next.SpanStart || nextStart.Lead.Length > 0))
        {
            changes.Add(new Change(nextStart.Start, Math.Max(nextStart.End, next.SpanStart), nextStart.Lead, ReplaceOrder));
        }
    }

    private Change GetRemoval(MarkdownBlockSyntax first, MarkdownBlockSyntax last, MarkdownBlockSyntax? next, out int nextLine, out bool wholeLines)
    {
        wholeLines = false;
        var lineStart = GetLineStart(_text, first.SpanStart);
        (var lineEnd, nextLine) = GetEndOfLastLine(_text, last);

        // The line of the kind of an alert stays
        if (GetAlertKind(first) is { } kind)
            return next is null ? new Change(kind.Span.End, lineEnd, "", ReplaceOrder) : new Change(kind.Span.End, GetEndOfPreviousLine(_text, GetLineStart(_text, next.SpanStart)), "", ReplaceOrder);

        // The marker of a container that starts on the line of the block, such as a list item, stays: the next block of the
        // container moves to the line of the marker, so that it stays in the container
        if (_text.AsSpan(lineStart, first.SpanStart - lineStart).ContainsAnyExcept(" \t>"))
        {
            var (start, _, lead, _) = GetEditStart(first);
            if (next is null)
                return new Change(start, lineEnd, lead, ReplaceOrder);

            // The marker and the first line of the next block can be a thematic break, such as - and - -: the next block stays on its line
            var nextStart = GetEditStart(next).Start;
            var nextLineEnd = GetEndOfLine(_text, nextStart);
            var line = string.Concat(_text.AsSpan(lineStart, start - lineStart), lead, _text.AsSpan(nextStart, nextLineEnd - nextStart));
            return IsThematicBreak(line)
                ? new Change(start, GetEndOfPreviousLine(_text, GetLineStart(_text, nextStart)), lead, ReplaceOrder)
                : new Change(start, nextStart, lead, ReplaceOrder);
        }

        // The lines after a block quote or a footer that the blocks end with, which have only their markers, are removed with them, or
        // they would start a new one
        var prefix = GetContinuationPrefix(first);
        if (last.DescendantNodesAndSelf().Any(node => node is MarkdownBlockQuoteSyntax or MarkdownAlertSyntax or MarkdownFooterSyntax && node.Span.End == last.Span.End))
        {
            var nextBlockLine = next is null ? int.MaxValue : GetLineStart(_text, next.SpanStart);
            while (nextLine < _text.Length && nextLine < nextBlockLine)
            {
                var line = _text.AsSpan(nextLine, GetEndOfLine(_text, nextLine) - nextLine);
                if (line.ContainsAnyExcept(" \t>^") || IsBlankLine(nextLine, prefix))
                    break;

                nextLine = GetNextLineStart(_text, nextLine);
            }
        }

        // The lines around the blocks can be read as one block once the blocks are removed, such as two paragraphs, or a paragraph and
        // a thematic break that makes it a setext heading: a blank line separates them. The items of a list, of a definition list and
        // the footnotes start with a marker, which separates them. A list item, a footnote or a definition can start with a blank line,
        // but not with two: the line of its marker is not separated from its first block, and the blank lines after the removed blocks
        // are removed with them.
        wholeLines = true;
        var replacement = "";
        var previousLine = lineStart > 0 ? GetLineStart(_text, GetEndOfPreviousLine(_text, lineStart)) : -1;
        var opensParent = first.Parent is MarkdownListItemSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownDefinitionItemSyntax && first.Parent.ChildNodes().OfType<MarkdownBlockSyntax>().First() == first && GetLineStart(_text, GetMarkerStart((MarkdownBlockSyntax)first.Parent)) == previousLine;
        if (opensParent && next is not null)
        {
            nextLine = GetLineStart(_text, next.SpanStart);
        }

        if (first is not (MarkdownListItemSyntax or MarkdownDefinitionItemSyntax or MarkdownFootnoteDefinitionSyntax)
            && !opensParent
            && lineStart > 0
            && nextLine < _text.Length
            && !IsBlankLine(previousLine, prefix)
            && !IsBlankLine(nextLine, prefix))
        {
            replacement = prefix.TrimEnd() + _newLine;
        }

        return new Change(lineStart, nextLine, replacement, ReplaceOrder);
    }

    // Whether the line, after the markers of block quotes, is a thematic break: 3 or more -, * or _ and whitespace
    private static bool IsThematicBreak(string line)
    {
        var content = line.AsSpan().TrimStart(" \t>");
        var count = 0;
        var marker = '\0';
        foreach (var c in content)
        {
            if (c is ' ' or '\t')
                continue;

            if (c is not ('-' or '*' or '_') || (marker != '\0' && c != marker))
                return false;

            marker = c;
            count++;
        }

        return count >= 3;
    }

    // Where the line after the line at lineStart starts
    private static int GetNextLineStart(string text, int lineStart)
    {
        var end = GetEndOfLine(text, lineStart);
        if (end < text.Length)
        {
            end += text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
        }

        return end;
    }

    // Whether the line is blank in the containers of the prefix: it has only whitespace and their markers, and no marker of a block quote
    // or of a footer in them, which the line would continue
    private bool IsBlankLine(int lineStart, string prefix)
    {
        var line = _text.AsSpan(lineStart, GetEndOfLine(_text, lineStart) - lineStart);
        return !line.ContainsAnyExcept(" \t>^") && line.Count('>') <= prefix.Count(c => c == '>') && line.Count('^') <= prefix.Count(c => c == '^');
    }

    // The ranges of the attributes for the block: an attributes paragraph with its lines, and other attributes with the whitespace
    // before them on their line, or after them when they start the line
    private List<Change> GetAttributesRemovals(MarkdownBlockSyntax block)
    {
        var removals = new List<Change>();
        _attributesTargets ??= GetAttributesTargets(OriginalTree.GetRoot());
        if (!_attributesTargets.TryGetValue(block, out var targets))
            return removals;

        var text = _text;
        foreach (var attributes in targets.Distinct())
        {
            // The lines of a paragraph of attributes are removed, unless it is the block
            if (attributes.Parent is MarkdownParagraphSyntax paragraph && paragraph != block && paragraph.ChildNodesAndTokens().Count == 1)
            {
                var lineStart = GetLineStart(text, paragraph.SpanStart);
                var (lineEnd, nextLine) = GetEndOfLastLine(text, paragraph);
                var hasListMarker = text.AsSpan(lineStart, paragraph.SpanStart - lineStart).ContainsAnyExcept(" \t>");
                removals.Add(hasListMarker ? new Change(paragraph.SpanStart, lineEnd, "", ReplaceOrder) : new Change(lineStart, nextLine, "", ReplaceOrder));
                continue;
            }

            // The attributes end with the line ending after them, which separates the text of the lines around them
            var start = attributes.SpanStart;
            var end = attributes.Span.End;
            if (end > start && text[end - 1] == '\n')
            {
                end--;
            }

            if (end > start && text[end - 1] == '\r')
            {
                end--;
            }

            while (start > 0 && text[start - 1] is ' ' or '\t')
            {
                start--;
            }

            if (start == 0 || text[start - 1] is '\r' or '\n')
            {
                start = attributes.SpanStart;
                while (end < text.Length && text[end] is ' ' or '\t')
                {
                    end++;
                }
            }

            removals.Add(new Change(start, end, "", ReplaceOrder));
        }

        return removals;
    }

    // Where the attributes can be written, in the order they are tried: none when there are no attributes
    private IEnumerable<Change?> GetAttributesInsertions(MarkdownBlockSyntax block, string? markdown)
    {
        if (markdown is null)
        {
            yield return null;
            yield break;
        }

        var text = _text;

        // At the end of a line of the block
        int? lineEnd = block switch
        {
            MarkdownHeadingSyntax { IsSetext: true } heading => GetEndOfPreviousLine(text, GetLineStart(text, heading.UnderlineToken.SpanStart)),
            MarkdownHeadingSyntax or MarkdownCodeBlockSyntax { IsFenced: true } or MarkdownMathBlockSyntax or MarkdownCustomContainerSyntax => GetEndOfLine(text, block.SpanStart),
            MarkdownParagraphSyntax paragraph => GetEndOfLastLine(text, paragraph).LineEnd,
            _ => null,
        };

        if (lineEnd is { } position)
        {
            yield return new Change(position, position, " " + markdown, InsertBeforeOrder);
        }

        // On a line before the block, with the markers of its containers, and a blank line before it or after it when it is needed
        var lineStart = GetLineStart(text, block.SpanStart);
        var (start, end, lead, _) = GetEditStart(block);
        var prefix = GetContinuationPrefix(block);
        var blank = prefix.TrimEnd();
        yield return new Change(start, end, lead + markdown + _newLine + prefix, InsertBeforeOrder);
        yield return new Change(start, end, lead + markdown + _newLine + blank + _newLine + prefix, InsertBeforeOrder);

        // The blank line before is written with the markers of the line without trailing whitespace, unless the line has the marker of
        // a list item, which stays on it
        var hasListMarker = text.AsSpan(lineStart, start - lineStart).ContainsAnyExcept(" \t>");
        foreach (var after in (string[])[prefix, blank + _newLine + prefix])
        {
            yield return hasListMarker
                ? new Change(start, end, lead + _newLine + prefix + markdown + _newLine + after, InsertBeforeOrder)
                : new Change(lineStart, end, blank + _newLine + prefix + markdown + _newLine + after, ReplaceOrder);
        }
    }

    private static bool Overlaps(List<Change> changes, Change change)
    {
        foreach (var other in changes)
        {
            if (change.Start < other.End && other.Start < change.End)
                return true;

            // An insertion in a range
            if (change.Start == change.End && other.Start < change.Start && change.Start < other.End)
                return true;

            if (other.Start == other.End && change.Start < other.Start && other.Start < change.End)
                return true;
        }

        return false;
    }

    private void EnsureBlockOfTree(MarkdownBlockSyntax block)
    {
        if (block.SyntaxTree != OriginalTree)
        {
            throw new ArgumentException("The block is not a block of this tree.", nameof(block));
        }
    }

    // The fence that closes a fenced block that the end of its container closes, or null
    private static string? GetMissingClosingFence(MarkdownBlockSyntax block) => block switch
    {
        MarkdownCodeBlockSyntax { IsFenced: true } code when code.ClosingFenceToken.RawKind == 0 => code.OpeningFenceToken.Text,
        MarkdownMathBlockSyntax math when math.ClosingFenceToken.RawKind == 0 => math.OpeningFenceToken.Text,
        MarkdownCustomContainerSyntax container when container.ClosingFenceToken.RawKind == 0 => container.OpeningFenceToken.Text,
        MarkdownFigureSyntax figure when figure.ClosingFenceToken.RawKind == 0 => figure.OpeningFenceToken.Text,
        _ => null,
    };

    // Where an edit of the block starts on its first line: where the block starts, after the markers of its containers. The
    // indentation of 4 columns or more of the block, such as the one of an indented code block, is part of the block: the edit starts
    // before it. When a container takes a part of a tab, the edit starts at the tab, and the lead is the spaces of the part the
    // container takes. The first line of a paragraph can lack the markers of its containers, when it continues a link reference
    // definition: the edit replaces its start with them. The first paragraph of an alert starts with the kind of the alert, which
    // stays: the edit starts on a new line after it. The text from Start to End is replaced.
    private (int Start, int End, string Lead, bool AfterLine) GetEditStart(MarkdownBlockSyntax block)
    {
        if (GetAlertKind(block) is { } kind)
            return (kind.Span.End, kind.Span.End, _newLine + GetContinuationPrefix(block), true);

        var lineStart = GetLineStart(_text, block.SpanStart);
        var cursor = ConsumeContainers(lineStart, GetContainers(block));
        if (cursor.Lazy)
            return (lineStart, block.SpanStart, GetContinuationPrefix(block), false);

        // A table can start after whitespace that is not indentation, such as a form feed, which another block would read as text
        var gap = cursor.Position < block.SpanStart ? _text.AsSpan(cursor.Position, block.SpanStart - cursor.Position) : [];
        if (gap.ContainsAnyExcept(" \t") && gap.IsWhiteSpace())
            return cursor.PartialTab ? (cursor.Position, block.SpanStart, new string(' ', cursor.Column - cursor.TabStartColumn), false) : (cursor.Position, block.SpanStart, "", false);

        if (cursor.Position > block.SpanStart || cursor.GetIndentation(block.SpanStart) < CodeIndent)
            return (block.SpanStart, block.SpanStart, "", false);

        return cursor.PartialTab ? (cursor.Position, cursor.Position, new string(' ', cursor.Column - cursor.TabStartColumn), false) : (cursor.Position, cursor.Position, "", false);
    }

    // The kind that starts the first paragraph of an alert
    private static SyntaxToken? GetAlertKind(MarkdownBlockSyntax block)
        => block is MarkdownParagraphSyntax { Parent: MarkdownAlertSyntax } && block.GetFirstToken() is { RawKind: (int)SyntaxKind.AlertKindToken } kind ? kind : null;

    // The markers of the containers of the block, as they continue on the next lines: the > of a block quote, the ^^ of a footer, and
    // the indentation of the content of a list item, of a footnote and of a definition
    private string GetContinuationPrefix(MarkdownBlockSyntax block)
    {
        var prefix = new StringBuilder();
        foreach (var container in GetContainers(block))
        {
            switch (container)
            {
                case MarkdownBlockQuoteSyntax or MarkdownAlertSyntax:
                    prefix.Append("> ");
                    break;
                case MarkdownFooterSyntax:
                    prefix.Append("^^ ");
                    break;
                case MarkdownListItemSyntax item:
                    prefix.Append(' ', GetItemWidth(item));
                    break;
                default:
                    prefix.Append(' ', CodeIndent);
                    break;
            }
        }

        return prefix.ToString();
    }

    // The containers whose markers or indentation are on the lines of the node, from the outermost
    private static List<MarkdownBlockSyntax> GetContainers(SyntaxNode node)
    {
        var containers = new List<MarkdownBlockSyntax>();
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is MarkdownBlockQuoteSyntax or MarkdownAlertSyntax or MarkdownListItemSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownDefinitionItemSyntax or MarkdownFooterSyntax)
            {
                containers.Add((MarkdownBlockSyntax)ancestor);
            }
        }

        containers.Reverse();
        return containers;
    }

    // The columns that a list item takes on its next lines: the indentation of its marker, its marker and the spaces after it
    private int GetItemWidth(MarkdownListItemSyntax item)
    {
        if (!_itemWidths.ContainsKey(item))
        {
            // Reading the line of the marker computes the width
            var containers = GetContainers(item);
            containers.Add(item);
            _ = ConsumeContainers(GetLineStart(_text, item.MarkerToken.SpanStart), containers);
        }

        // The marker and a space, when the line is not read as the parser read it
        return _itemWidths.TryGetValue(item, out var width) ? width : item.MarkerToken.Span.Length + 1;
    }

    // Reads the markers and the indentation of the containers at the start of a line, as the parser does: the cursor is where their content starts
    private LineCursor ConsumeContainers(int lineStart, List<MarkdownBlockSyntax> containers)
    {
        var cursor = new LineCursor(_text, lineStart, GetEndOfLine(_text, lineStart));
        foreach (var container in containers)
        {
            // A container that starts on the line is after the markers of the containers that the line continues
            var marker = GetMarkerStart(container);
            var opened = marker >= lineStart && marker <= cursor.LineEnd;
            if (opened)
            {
                cursor.Lazy = false;
            }

            switch (container)
            {
                case MarkdownBlockQuoteSyntax or MarkdownAlertSyntax:
                    cursor.ConsumeMarker(">");
                    break;

                case MarkdownFooterSyntax:
                    cursor.ConsumeMarker("^^");
                    break;

                case MarkdownListItemSyntax item when opened:
                    _itemWidths[item] = cursor.ConsumeListMarker(item.MarkerToken.SpanStart, item.MarkerToken.Span.Length);
                    break;

                case MarkdownListItemSyntax item:
                    var width = GetItemWidth(item);
                    if (cursor.IsBlank)
                    {
                        cursor.AdvanceNextNonspace();
                    }
                    else if (cursor.Indent >= width)
                    {
                        cursor.Advance(width, columns: true);
                    }
                    else
                    {
                        cursor.Lazy = true;
                    }

                    break;

                case MarkdownFootnoteDefinitionSyntax footnote when opened:
                    var colon = footnote.ChildTokens().First(token => token.RawKind == (int)SyntaxKind.ColonToken);
                    cursor.AdvanceNextNonspace();
                    cursor.Advance(colon.Span.End - cursor.Position, columns: false);
                    break;

                case MarkdownDefinitionItemSyntax when opened:
                    var contentColumn = cursor.Column + CodeIndent;
                    cursor.AdvanceNextNonspace();
                    cursor.Advance(1, columns: false);
                    if (contentColumn > cursor.Column)
                    {
                        cursor.Advance(contentColumn - cursor.Column, columns: true);
                    }

                    break;

                default:
                    // A footnote or a definition continues on a line that is not indented, but not after a blank line
                    if (cursor.Indent >= CodeIndent)
                    {
                        cursor.Advance(CodeIndent, columns: true);
                    }
                    else if (!cursor.IsBlank)
                    {
                        cursor.Lazy = true;
                    }

                    break;
            }
        }

        return cursor;
    }

    // Where the marker that opens the container is: the line of the marker is the first line of the container
    private static int GetMarkerStart(MarkdownBlockSyntax container) => container switch
    {
        MarkdownListItemSyntax item => item.MarkerToken.SpanStart,
        MarkdownDefinitionItemSyntax item => item.MarkerToken.SpanStart,
        _ => container.SpanStart,
    };

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static int GetEndOfLine(string text, int position)
    {
        while (position < text.Length && text[position] is not ('\r' or '\n'))
        {
            position++;
        }

        return position;
    }

    // Where the line before the line at lineStart ends, before its line ending
    private static int GetEndOfPreviousLine(string text, int lineStart)
    {
        var position = lineStart - 1;
        if (position > 0 && text[position] == '\n' && text[position - 1] == '\r')
        {
            position--;
        }

        return position;
    }

    private static int GetLineStart(string text, int position)
    {
        while (position > 0 && text[position - 1] is not ('\r' or '\n'))
        {
            position--;
        }

        return position;
    }

    // Where the last line of the block ends, before its line ending, and where the next line starts
    private static (int LineEnd, int NextLine) GetEndOfLastLine(string text, MarkdownBlockSyntax block)
    {
        // The last token of a code block is a line with its line ending
        var end = block.Span.End;
        if (end > block.SpanStart && text[end - 1] is '\r' or '\n')
        {
            var lineEnd = end - 1;
            if (text[lineEnd] == '\n' && lineEnd > 0 && text[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }

            return (lineEnd, end);
        }

        while (end < text.Length && text[end] is not ('\r' or '\n'))
        {
            end++;
        }

        var next = end;
        if (next < text.Length)
        {
            next += text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n' ? 2 : 1;
        }

        return (end, next);
    }

    private static string GetNewLine(string text)
    {
        var index = text.AsSpan().IndexOfAny('\r', '\n');
        if (index < 0 || text[index] == '\n')
        {
            return "\n";
        }

        return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }

    private static string[] SplitLines(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    // A blank line gets the markers without the spaces after them, so that it has no trailing whitespace
    private static void AppendPrefixed(StringBuilder builder, string prefix, string line) => builder.Append(line.Length == 0 ? prefix.TrimEnd() : prefix).Append(line);

    // A change of the text: the range from Start to End is replaced with Text. Owner is the index of the attributes edit it is for,
    // and IsCandidate whether it writes the attributes at one of the places they can be written.
    private readonly record struct Change(int Start, int End, string Text, int Order)
    {
        public int Owner { get; init; } = -1;

        public bool IsCandidate { get; init; }
    }

    // The attributes given to a block: Removed when other attributes are given to it later
    private sealed record AttributesEdit(MarkdownBlockSyntax Block, MarkdownGenericAttributes Attributes, List<Change> Removals, List<Change?> Candidates, string PlainText)
    {
        public bool Removed { get; init; }
    }

    // A position on a line, which reads the markers of containers as the parser does: tabs advance to the next multiple of 4 columns,
    // and a container can take a part of a tab
    private struct LineCursor(string text, int position, int lineEnd)
    {
        public int Position { get; private set; } = position;

        public int LineEnd { get; } = lineEnd;

        public int Column { get; private set; }

        // Whether the markers or the indentation of a container are not on the line, a line that continues a paragraph
        public bool Lazy { get; set; }

        // Whether Position is a tab that a container took a part of, from TabStartColumn to Column
        public bool PartialTab { get; private set; }

        public int TabStartColumn { get; private set; }

        public readonly int Indent => GetNextNonspace().Column - Column;

        // The columns of whitespace from the cursor to a position of the line, or 0 when there is other text between them
        public readonly int GetIndentation(int position)
        {
            var column = Column;
            for (var index = Position; index < position && index < LineEnd; index++)
            {
                if (text[index] is not (' ' or '\t'))
                    return 0;

                column += text[index] == '\t' ? 4 - (column % 4) : 1;
            }

            return column - Column;
        }

        public readonly bool IsBlank => GetNextNonspace().Position >= LineEnd;

        private readonly char Peek(int index) => index < LineEnd ? text[index] : '\0';

        private readonly (int Position, int Column) GetNextNonspace()
        {
            var index = Position;
            var column = Column;
            while (index < LineEnd)
            {
                var c = text[index];
                if (c == ' ')
                {
                    column++;
                }
                else if (c == '\t')
                {
                    column += 4 - (column % 4);
                }
                else
                {
                    break;
                }

                index++;
            }

            return (index, column);
        }

        public void AdvanceNextNonspace()
        {
            (Position, Column) = GetNextNonspace();
            PartialTab = false;
        }

        public void Advance(int count, bool columns)
        {
            while (count > 0 && Position < LineEnd)
            {
                if (text[Position] == '\t')
                {
                    var tabStart = PartialTab ? TabStartColumn : Column;
                    var charsToTab = 4 - (Column % 4);
                    if (columns)
                    {
                        PartialTab = charsToTab > count;
                        var charsToAdvance = Math.Min(charsToTab, count);
                        Column += charsToAdvance;
                        Position += PartialTab ? 0 : 1;
                        TabStartColumn = tabStart;
                        count -= charsToAdvance;
                    }
                    else
                    {
                        PartialTab = false;
                        Column += charsToTab;
                        Position++;
                        count--;
                    }
                }
                else
                {
                    PartialTab = false;
                    Position++;
                    Column++;
                    count--;
                }
            }
        }

        // The marker of a block quote or of a footer, after at most 3 spaces, and the space after it
        public void ConsumeMarker(string marker)
        {
            var (next, _) = GetNextNonspace();
            if (Indent >= CodeIndent || !text.AsSpan(next, LineEnd - next).StartsWith(marker, StringComparison.Ordinal))
            {
                Lazy = true;
                return;
            }

            AdvanceNextNonspace();
            Advance(marker.Length, columns: false);
            if (Peek(Position) is ' ' or '\t')
            {
                Advance(1, columns: true);
            }
        }

        // The marker of a list item and the spaces after it, as the parser reads them: returns the columns that the item takes on its
        // next lines
        public int ConsumeListMarker(int markerStart, int markerLength)
        {
            var markerOffset = Indent;
            AdvanceNextNonspace();

            // The markers of the containers before are not read as the parser read them: the marker is read where it is
            if (Position != markerStart)
            {
                markerOffset = 0;
                Advance(markerStart - Position, columns: false);
            }

            Advance(markerLength, columns: true);
            var spacesStartColumn = Column;
            var spacesStart = this;
            do
            {
                Advance(1, columns: true);
            }
            while (Column - spacesStartColumn < 5 && Peek(Position) is ' ' or '\t');

            var blankItem = Position >= LineEnd;
            var spacesAfterMarker = Column - spacesStartColumn;
            int padding;
            if (spacesAfterMarker >= 5 || spacesAfterMarker < 1 || blankItem)
            {
                // The content starts after one space: the other spaces are the indentation of an indented code block
                padding = markerLength + 1;
                this = spacesStart;
                if (Peek(Position) is ' ' or '\t')
                {
                    Advance(1, columns: true);
                }
            }
            else
            {
                padding = markerLength + spacesAfterMarker;
            }

            return markerOffset + padding;
        }
    }
}
