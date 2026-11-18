using System.Text;
using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>A parsed Markdown document.</summary>
/// <remarks>
/// Parsing never throws: whatever the text says, the tree reproduces it exactly. The characters that no token covers,
/// such as the markers of a block quote on its continuation lines, the indentation and the line endings, are trivia.
/// </remarks>
public sealed class MarkdownSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly MarkdownDocumentSyntax _root;

    private MarkdownSyntaxTree(SourceText text, MarkdownParseOptions options, MarkdownDocumentSyntax root, string? path, MarkdownSyntaxTree? container = null, CellContext? cell = null)
    {
        _text = text;
        _root = root;
        Options = options;
        FilePath = path;
        Container = container;
        _cell = cell;
        _root.AttachToTree(this);
    }

    // The tree of the grid table of a cell, whose definitions the cell can use
    internal MarkdownSyntaxTree? Container { get; }

    // For the content of a cell, how it was parsed, which the trees made from it keep
    private readonly CellContext? _cell;

    // The content of a cell of a grid table of the tree. Markdig does not close the blocks of a cell that the last line of the table
    // spans: a grid table that ends it is kept, even when it has no row.
    // For the content of a cell, the blocks it is in, in this tree and the trees around it
    internal int CellDepth => _cell?.Depth ?? 0;

    internal static MarkdownSyntaxTree ParseCell(string text, MarkdownSyntaxTree? container, bool isClosed, int depth)
    {
        var options = container?.Options ?? MarkdownParseOptions.Default;
        string[] labels = [.. container?.GetRoot().LinkReferenceDefinitions.Select(definition => Scanners.NormalizeLabel(definition.Label)) ?? []];
        var cell = new CellContext(labels, KeepsGridTable: !isClosed, depth);
        var green = MarkdownParser.Parse(text, options, cell);
        return new MarkdownSyntaxTree(SourceText.From(text), options, (MarkdownDocumentSyntax)green.CreateRed(parent: null, position: 0), container?.FilePath, container, cell);
    }

    // Parses a new text as this tree was: the content of a cell keeps the definitions of its table
    internal MarkdownSyntaxTree Reparse(SourceText text)
    {
        if (_cell is null)
            return ParseText(text, Options, FilePath);

        var green = MarkdownParser.Parse(text.ToString(), Options, _cell);
        return new MarkdownSyntaxTree(text, Options, (MarkdownDocumentSyntax)green.CreateRed(parent: null, position: 0), FilePath, Container, _cell);
    }

    public override string? FilePath { get; }

    /// <summary>Gets the options the text was parsed with.</summary>
    public MarkdownParseOptions Options { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the document node.</summary>
    public new MarkdownDocumentSyntax GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/> as CommonMark.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static MarkdownSyntaxTree ParseText(string text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The Markdown to read.</param>
    /// <param name="options">How to read it, or <see langword="null"/> for <see cref="MarkdownParseOptions.Default"/>.</param>
    /// <param name="path">Where the text came from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static MarkdownSyntaxTree ParseText(string text, MarkdownParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), options, path);
    }

    /// <summary>Parses <paramref name="text"/> as CommonMark.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static MarkdownSyntaxTree ParseText(SourceText text, string? path = null) => ParseText(text, options: null, path);

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <param name="text">The Markdown to read.</param>
    /// <param name="options">How to read it, or <see langword="null"/> for <see cref="MarkdownParseOptions.Default"/>.</param>
    /// <param name="path">Where the text came from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static MarkdownSyntaxTree ParseText(SourceText text, MarkdownParseOptions? options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        options ??= MarkdownParseOptions.Default;
        var green = MarkdownParser.Parse(text.ToString(), options);
        return new MarkdownSyntaxTree(text, options, (MarkdownDocumentSyntax)green.CreateRed(parent: null, position: 0), path);
    }

    /// <summary>Inserts blocks before <paramref name="block"/>, in its container.</summary>
    /// <param name="block">The block of this tree to insert before.</param>
    /// <param name="markdown">The blocks to insert, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <returns>A tree parsed from the new text.</returns>
    /// <remarks>
    /// The text is inserted as it is: separate it from the block with a blank line when the two would otherwise be read as
    /// one, such as two paragraphs. In a list, a blank line makes the list loose.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree InsertBlocksBefore(MarkdownBlockSyntax block, string markdown)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(markdown);
        EnsureBlockOfThisTree(block);

        // The first line goes where the block starts, after the markers of its line, and the block moves to a line of its own
        var text = _text.Text;
        var prefix = GetContinuationPrefix(text, block.SpanStart);
        var newLine = GetNewLine(text);
        var lines = SplitLines(markdown);
        var inserted = new StringBuilder();
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

            inserted.Append(newLine);
        }

        inserted.Append(prefix);
        return WithText(text.Insert(block.SpanStart, inserted.ToString()));
    }

    /// <summary>Inserts blocks after <paramref name="block"/>, in its container.</summary>
    /// <param name="block">The block of this tree to insert after.</param>
    /// <param name="markdown">The blocks to insert, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <returns>A tree parsed from the new text.</returns>
    /// <remarks>
    /// The lines are inserted after the last line of the block. Separate the text from the block with a blank line when
    /// the two would otherwise be read as one, such as two paragraphs: a line of text after a paragraph continues it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree InsertBlocksAfter(MarkdownBlockSyntax block, string markdown)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(markdown);
        EnsureBlockOfThisTree(block);

        var text = _text.Text;
        var prefix = GetContinuationPrefix(text, block.SpanStart);
        var newLine = GetNewLine(text);
        var (lineEnd, nextLine) = GetEndOfLastLine(text, block);
        var inserted = new StringBuilder();
        if (nextLine == lineEnd)
        {
            // The block ends the text without a line ending
            inserted.Append(newLine);
        }

        foreach (var line in SplitLines(markdown))
        {
            AppendPrefixed(inserted, prefix, line);
            inserted.Append(newLine);
        }

        if (nextLine == lineEnd)
        {
            inserted.Length -= newLine.Length;
        }

        return WithText(text.Insert(nextLine, inserted.ToString()));
    }

    /// <summary>Removes <paramref name="block"/> and its lines.</summary>
    /// <returns>A tree parsed from the new text.</returns>
    /// <remarks>
    /// The lines of the block are removed, with the markers of the containers on them. When the block starts on the line
    /// of the marker of its list item, the marker stays: removing the only block of an item leaves the item empty.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree RemoveBlock(MarkdownBlockSyntax block)
    {
        ArgumentNullException.ThrowIfNull(block);
        EnsureBlockOfThisTree(block);

        var text = _text.Text;
        var lineStart = GetLineStart(text, block.SpanStart);
        var (lineEnd, nextLine) = GetEndOfLastLine(text, block);
        var hasListMarker = text.AsSpan(lineStart, block.SpanStart - lineStart).ContainsAnyExcept(" \t>");
        return hasListMarker
            ? WithText(text.Remove(block.SpanStart, lineEnd - block.SpanStart))
            : WithText(text.Remove(lineStart, nextLine - lineStart));
    }

    /// <summary>Replaces <paramref name="block"/> with other blocks, in its container.</summary>
    /// <param name="block">The block of this tree to replace.</param>
    /// <param name="markdown">The blocks that replace it, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <returns>A tree parsed from the new text.</returns>
    /// <remarks>
    /// The text replaces the lines of the block, from where it starts, after the markers of its line, to the end of its last
    /// line. The line ending after the block stays, and a line ending at the end of <paramref name="markdown"/> is ignored. Separate
    /// the text from the blocks around it with a blank line when they would otherwise be read as one, such as two paragraphs.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree ReplaceBlock(MarkdownBlockSyntax block, string markdown)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(markdown);
        EnsureBlockOfThisTree(block);

        var text = _text.Text;
        var prefix = GetContinuationPrefix(text, block.SpanStart);
        var newLine = GetNewLine(text);
        var (lineEnd, _) = GetEndOfLastLine(text, block);
        var lines = SplitLines(markdown);
        var replacement = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                replacement.Append(newLine);
                AppendPrefixed(replacement, prefix, lines[i]);
            }
            else
            {
                replacement.Append(lines[i]);
            }
        }

        return WithText(string.Concat(text.AsSpan(0, block.SpanStart), replacement.ToString(), text.AsSpan(lineEnd)));
    }

    /// <summary>Replaces the generic attributes of <paramref name="block"/>.</summary>
    /// <param name="block">The block of this tree whose attributes are replaced.</param>
    /// <param name="attributes">The attributes of the block, or <see langword="null"/> to remove them.</param>
    /// <returns>A tree parsed from the new text, where <see cref="MarkdownBlockSyntax.GetGenericAttributes"/> of the block returns <paramref name="attributes"/>.</returns>
    /// <remarks>
    /// <para>
    /// Every <c>{...}</c> that is for the block is removed, and the attributes are written as Markdig reads them: at the end of
    /// the first line of a heading, of a fenced code block, of a math block and of a custom container, at the end of the last
    /// line of a paragraph, and on a line before the other blocks, such as a list, a block quote or a table, with the markers of
    /// their containers. A blank line separates this line from the paragraph before it, or from a thematic break that would make it a setext
    /// heading.
    /// </para>
    /// <para>The text is parsed again, and checked to give the attributes to the block.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="block"/> is not a block of this tree, or <paramref name="attributes"/> cannot be written, such as a class with
    /// whitespace or an attribute name that starts with a digit.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The tree is parsed without <see cref="MarkdownExtensions.GenericAttributes"/>, or the block cannot have these attributes, such as
    /// an HTML block, a list item or a footnote definition, which Markdig gives no attributes to.
    /// </exception>
    public MarkdownSyntaxTree WithGenericAttributes(MarkdownBlockSyntax block, MarkdownGenericAttributes? attributes)
    {
        ArgumentNullException.ThrowIfNull(block);
        EnsureBlockOfThisTree(block);
        if (!Options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes))
            throw new InvalidOperationException("The tree is parsed without generic attributes.");

        attributes ??= MarkdownGenericAttributes.Empty;
        var markdown = attributes.IsEmpty ? null : attributes.ToMarkdown(nameof(attributes));
        if (block.GetGenericAttributes().Equals(attributes))
            return this;

        var text = _text.Text;
        var removals = GetAttributesRemovals(text, block);
        foreach (var insertion in GetAttributesInsertions(text, block, markdown))
        {
            var changes = new List<(int Start, int End, string Text)>(removals);
            if (insertion is { } change)
            {
                changes.Add(change);
            }

            // The changes are applied together: they cannot overlap
            changes.Sort((left, right) => left.Start != right.Start ? left.Start.CompareTo(right.Start) : left.End.CompareTo(right.End));
            if (changes.Zip(changes.Skip(1)).Any(pair => pair.Second.Start < pair.First.End))
            {
                continue;
            }

            // The block starts after the changes before it, an insertion where it starts included
            var blockStart = block.SpanStart;
            var updatedText = new StringBuilder(text);
            foreach (var (start, end, replacement) in changes.OrderByDescending(change => change.Start).ThenByDescending(change => change.End))
            {
                updatedText.Remove(start, end - start).Insert(start, replacement);
                if (end <= block.SpanStart)
                {
                    blockStart += replacement.Length - (end - start);
                }
            }

            var updated = WithText(updatedText.ToString());
            if (updated.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().FirstOrDefault(node => node.SpanStart == blockStart && node.RawKind == block.RawKind) is { } updatedBlock && updatedBlock.GetGenericAttributes().Equals(attributes))
                return updated;
        }

        throw new InvalidOperationException($"The {block.Kind()} cannot have these generic attributes: the text written for them is not read as attributes of the block.");
    }

    // The ranges of the attributes for the block: an attributes paragraph with its lines, and other attributes with the whitespace
    // before them on their line, or after them when they start the line
    private List<(int Start, int End, string Text)> GetAttributesRemovals(string text, MarkdownBlockSyntax block)
    {
        var removals = new List<(int Start, int End, string Text)>();
        if (!_root.HasGenericAttributes)
            return removals;

        var found = new HashSet<MarkdownGenericAttributesSyntax>();
        _ = MarkdownGenericAttributesSyntax.FindTargets(_root, (attributes, target) =>
        {
            if (target != block || !found.Add(attributes))
            {
                return;
            }

            // The lines of a paragraph of attributes are removed, unless it is the block
            if (attributes.Parent is MarkdownParagraphSyntax paragraph && paragraph != block && paragraph.ChildNodesAndTokens().Count == 1)
            {
                var lineStart = GetLineStart(text, paragraph.SpanStart);
                var (lineEnd, nextLine) = GetEndOfLastLine(text, paragraph);
                var hasListMarker = text.AsSpan(lineStart, paragraph.SpanStart - lineStart).ContainsAnyExcept(" \t>");
                removals.Add(hasListMarker ? (paragraph.SpanStart, lineEnd, "") : (lineStart, nextLine, ""));
                return;
            }

            var start = attributes.SpanStart;
            var end = attributes.Span.End;
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

            removals.Add((start, end, ""));
        });

        return removals;
    }

    // Where the attributes can be written, in the order they are tried: none when there are no attributes
    private static IEnumerable<(int Start, int End, string Text)?> GetAttributesInsertions(string text, MarkdownBlockSyntax block, string? markdown)
    {
        if (markdown is null)
        {
            yield return null;
            yield break;
        }

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
            yield return (position, position, " " + markdown);
        }

        // On a line before the block, with the markers of its containers, and a blank line before it or after it when it is needed
        // An indented code block starts after its indentation, which the line of the attributes does not have
        var lineStart = GetLineStart(text, block.SpanStart);
        var start = block.SpanStart;
        if (block is MarkdownCodeBlockSyntax { IsFenced: false })
        {
            var columns = 0;
            while (start > lineStart && columns < 4 && text[start - 1] is ' ' or '\t')
            {
                columns = text[start - 1] == '\t' ? 4 : columns + 1;
                start--;
            }
        }

        var prefix = GetContinuationPrefix(text, start);
        var blank = prefix.TrimEnd();
        var newLine = GetNewLine(text);
        yield return (start, start, markdown + newLine + prefix);
        yield return (start, start, markdown + newLine + blank + newLine + prefix);

        // The blank line before is written with the markers of the line without trailing whitespace, unless the line has the marker of
        // a list item, which stays on it
        var hasListMarker = text.AsSpan(lineStart, start - lineStart).ContainsAnyExcept(" \t>");
        foreach (var after in (string[])[prefix, blank + newLine + prefix])
        {
            yield return hasListMarker
                ? (start, start, newLine + prefix + markdown + newLine + after)
                : (lineStart, start, blank + newLine + prefix + markdown + newLine + after);
        }
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

    protected override SyntaxNode GetRootCore() => _root;

    private MarkdownSyntaxTree WithText(string text) => Reparse(SourceText.From(text));

    private void EnsureBlockOfThisTree(MarkdownBlockSyntax block)
    {
        if (block.SyntaxTree != this)
        {
            throw new ArgumentException("The block is not a block of this tree.", nameof(block));
        }
    }

    private static int GetLineStart(string text, int position)
    {
        while (position > 0 && text[position - 1] is not ('\r' or '\n'))
        {
            position--;
        }

        return position;
    }

    // The markers of the containers before the block on its line, as they continue on the next lines: a list marker becomes
    // the spaces of the indentation of its item, and the > of a block quote stays
    private static string GetContinuationPrefix(string text, int blockStart)
    {
        var lineStart = GetLineStart(text, blockStart);
        var prefix = text.ToCharArray(lineStart, blockStart - lineStart);
        for (var i = 0; i < prefix.Length; i++)
        {
            if (prefix[i] is not (' ' or '\t' or '>'))
            {
                prefix[i] = ' ';
            }
        }

        return new string(prefix);
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

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => Reparse(newText);

    // A document that belongs to another tree gets a new red node, so that its nodes know the tree they are in; a root that
    // is not a document, such as a paragraph, is parsed as one
    protected override SyntaxTree WithRootCore(SyntaxNode root) => root switch
    {
        MarkdownDocumentSyntax { SyntaxTree: null } document => new MarkdownSyntaxTree(SourceText.From(root.ToFullString()), Options, document, FilePath, Container, _cell),
        MarkdownDocumentSyntax document => new MarkdownSyntaxTree(SourceText.From(root.ToFullString()), Options, (MarkdownDocumentSyntax)document.Green.CreateRed(parent: null, position: 0), FilePath, Container, _cell),
        _ => Reparse(SourceText.From(root.ToFullString())),
    };
}
