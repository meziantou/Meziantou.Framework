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
    internal static MarkdownSyntaxTree ParseCell(string text, MarkdownSyntaxTree? container, bool isClosed)
    {
        var options = container?.Options ?? MarkdownParseOptions.Default;
        string[] labels = [.. container?.GetRoot().LinkReferenceDefinitions.Select(definition => Scanners.NormalizeLabel(definition.Label)) ?? []];
        var cell = new CellContext(labels, KeepsGridTable: !isClosed);
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
