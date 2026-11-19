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

    // The normalized labels of the link reference definitions and of the footnotes that the cells of the grid tables of the tree
    // use: the ones of the tree and of the trees around it. They are read once for all the cells of the tree.
    private CellLabels? _cellLabels;

    private sealed record CellLabels(string[] References, string[] Footnotes);

    private CellLabels GetCellLabels()
    {
        if (_cellLabels is { } labels)
        {
            return labels;
        }

        var outer = Container?.GetCellLabels();
        var root = GetRoot();
        string[] references = [.. root.LinkReferenceDefinitions.Select(definition => Scanners.NormalizeLabel(definition.Label)), .. outer?.References ?? []];
        string[] footnotes = Options.Extensions.HasFlag(MarkdownExtensions.Footnotes) ? [.. root.Blocks.OfType<MarkdownFootnoteDefinitionSyntax>().Select(definition => Scanners.NormalizeLabel(definition.Label)), .. outer?.Footnotes ?? []] : [];
        labels = new CellLabels(references, footnotes);
        _cellLabels = labels;
        return labels;
    }

    internal static MarkdownSyntaxTree ParseCell(string text, MarkdownSyntaxTree? container, bool isClosed, int depth)
    {
        var options = container?.Options ?? MarkdownParseOptions.Default;
        var labels = container?.GetCellLabels();
        var cell = new CellContext(labels?.References ?? [], labels?.Footnotes ?? [], KeepsGridTable: !isClosed, depth);
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
    /// one, such as two paragraphs. In a list, a blank line makes the list loose. To change several blocks, use a
    /// <see cref="MarkdownSyntaxTreeEditor"/>, which parses the text once.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree InsertBlocksBefore(MarkdownBlockSyntax block, string markdown)
    {
        var editor = new MarkdownSyntaxTreeEditor(this);
        editor.InsertBlocksBefore(block, markdown);
        return editor.GetChangedTree();
    }

    /// <summary>Inserts blocks after <paramref name="block"/>, in its container.</summary>
    /// <param name="block">The block of this tree to insert after.</param>
    /// <param name="markdown">The blocks to insert, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <returns>A tree parsed from the new text.</returns>
    /// <remarks>
    /// The lines are inserted after the last line of the block. Separate the text from the block with a blank line when
    /// the two would otherwise be read as one, such as two paragraphs: a line of text after a paragraph continues it. A fenced
    /// block that the end of its container closes, such as a code block without its closing fence, gets a closing fence first.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree InsertBlocksAfter(MarkdownBlockSyntax block, string markdown)
    {
        var editor = new MarkdownSyntaxTreeEditor(this);
        editor.InsertBlocksAfter(block, markdown);
        return editor.GetChangedTree();
    }

    /// <summary>Removes <paramref name="block"/> and its lines.</summary>
    /// <returns>A tree parsed from the new text.</returns>
    /// <remarks>
    /// <para>
    /// The lines of the block are removed, with the markers of the containers on them. A blank line, with the markers of the
    /// containers, replaces them when the lines around them are not blank, so that the blocks around the removed one are not read as
    /// one, such as two paragraphs, or a paragraph and a thematic break that would make it a setext heading.
    /// </para>
    /// <para>
    /// When the block starts on the line of the marker of its list item, the marker stays, and the next block of the item moves to
    /// this line: removing the only block of an item leaves the item empty.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree RemoveBlock(MarkdownBlockSyntax block)
    {
        var editor = new MarkdownSyntaxTreeEditor(this);
        editor.RemoveBlock(block);
        return editor.GetChangedTree();
    }

    /// <summary>Replaces <paramref name="block"/> with other blocks, in its container.</summary>
    /// <param name="block">The block of this tree to replace.</param>
    /// <param name="markdown">The blocks that replace it, as Markdown that is not in a container: the markers of the containers of <paramref name="block"/> are added to each of its lines.</param>
    /// <returns>A tree parsed from the new text.</returns>
    /// <remarks>
    /// The text replaces the lines of the block, from where it starts, after the markers of its line, to the end of its last
    /// line: the indentation of an indented code block is replaced too. The line ending after the block stays, and a line ending at
    /// the end of <paramref name="markdown"/> is ignored. Separate the text from the blocks around it with a blank line when they
    /// would otherwise be read as one, such as two paragraphs.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="block"/> or <paramref name="markdown"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="block"/> is not a block of this tree.</exception>
    public MarkdownSyntaxTree ReplaceBlock(MarkdownBlockSyntax block, string markdown)
    {
        var editor = new MarkdownSyntaxTreeEditor(this);
        editor.ReplaceBlock(block, markdown);
        return editor.GetChangedTree();
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
    /// <para>
    /// The text is parsed again, and checked to give the attributes to the block and to keep its text. To change the attributes
    /// of several blocks, use <see cref="MarkdownSyntaxTreeEditor.SetGenericAttributes(MarkdownBlockSyntax, MarkdownGenericAttributes?)"/>,
    /// which parses the text once, or, in a <see cref="MarkdownSyntaxRewriter"/>,
    /// <see cref="MarkdownBlockSyntax.WithGenericAttributes(MarkdownGenericAttributes?)"/>, which changes a block whose attributes are
    /// written in it without parsing the document again.
    /// </para>
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
        var editor = new MarkdownSyntaxTreeEditor(this);
        editor.SetGenericAttributes(block, attributes);
        return editor.GetChangedTree();
    }

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => Reparse(newText);

    // The text of the root is parsed again: a node that was replaced, such as a token of a rewriter or the copy that a With method
    // returns, can change how the text around it is read, such as a blank line removed between two paragraphs. The root of this tree
    // is kept, as it was parsed from its text.
    protected override SyntaxTree WithRootCore(SyntaxNode root)
    {
        if (root == _root)
            return this;

        return Reparse(SourceText.From(root.ToFullString()));
    }
}
