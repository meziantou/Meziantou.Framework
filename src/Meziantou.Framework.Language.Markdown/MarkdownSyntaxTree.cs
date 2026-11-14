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

    private MarkdownSyntaxTree(SourceText text, MarkdownParseOptions options, MarkdownSyntaxNode root, string? path)
    {
        _text = text;
        _root = root;
        Options = options;
        FilePath = path;
        _root.AttachToTree(this);
    }

    public override string? FilePath { get; }

    /// <summary>Gets the options the text was parsed with.</summary>
    public MarkdownParseOptions Options { get; }

    public override SourceText GetText() => _text;

    /// <summary>Gets the document node.</summary>
    public new MarkdownSyntaxNode GetRoot() => _root;

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
        var green = MarkdownParser.Parse(text.ToString(), options.Extensions);
        return new MarkdownSyntaxTree(text, options, (MarkdownSyntaxNode)green.CreateRed(parent: null, position: 0), path);
    }

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText, Options, FilePath);

    protected override SyntaxTree WithRootCore(SyntaxNode root) => new MarkdownSyntaxTree(SourceText.From(root.ToFullString()), Options, (MarkdownSyntaxNode)root, FilePath);
}
