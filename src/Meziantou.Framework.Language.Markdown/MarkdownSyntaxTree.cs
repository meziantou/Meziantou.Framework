using Meziantou.Framework.Language.Markdown.Parsing;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>A parsed Markdown document.</summary>
/// <remarks>
/// Parsing never throws: whatever the text says, the tree reproduces it exactly. The characters that no token covers,
/// such as the markers of a block quote on its continuation lines, the indentation and the line endings, are trivia.
/// </remarks>
public sealed class MarkdownSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly MarkdownSyntaxNode _root;

    private MarkdownSyntaxTree(SourceText text, MarkdownSyntaxNode root, string? path)
    {
        _text = text;
        _root = root;
        FilePath = path;
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the document node.</summary>
    public new MarkdownSyntaxNode GetRoot() => _root;

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static MarkdownSyntaxTree ParseText(string text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ParseText(SourceText.From(text), path);
    }

    /// <summary>Parses <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static MarkdownSyntaxTree ParseText(SourceText text, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var green = MarkdownParser.Parse(text.ToString());
        return new MarkdownSyntaxTree(text, (MarkdownSyntaxNode)green.CreateRed(parent: null, position: 0), path);
    }

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root) => new MarkdownSyntaxTree(SourceText.From(root.ToFullString()), (MarkdownSyntaxNode)root, FilePath);
}
