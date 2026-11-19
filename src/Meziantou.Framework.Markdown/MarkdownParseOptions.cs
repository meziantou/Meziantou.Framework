namespace Meziantou.Framework.Markdown;

/// <summary>Configures how <see cref="MarkdownSyntaxTree"/> parses text.</summary>
public sealed record MarkdownParseOptions
{
    /// <summary>Gets the options used when none are given: CommonMark, without extensions.</summary>
    public static MarkdownParseOptions Default { get; } = new();

    /// <summary>Gets the options of GitHub Flavored Markdown: CommonMark and the extensions of GitHub.</summary>
    public static MarkdownParseOptions GitHubFlavored { get; } = new() { Extensions = MarkdownExtensions.GitHubFlavored };

    /// <summary>Gets the extensions to parse.</summary>
    public MarkdownExtensions Extensions { get; init; }

    /// <summary>Gets how <see cref="MarkdownExtensions.AutoIdentifiers"/> makes the identifiers of the headings.</summary>
    public MarkdownAutoIdentifierOptions AutoIdentifierOptions { get; init; } = MarkdownAutoIdentifierOptions.Default;

    /// <summary>Gets the shortcodes and the smileys that <see cref="MarkdownExtensions.Emoji"/> reads: by default, the shortcodes of GitHub and the common smileys.</summary>
    public MarkdownEmojiMapping EmojiMapping { get; init; } = MarkdownEmojiMapping.Default;

    /// <summary>Gets a value indicating whether a block quote in a container, such as a list item, can be an alert. By default, only the block quotes of the document are.</summary>
    public bool AllowNestedAlerts { get; init; }

    /// <summary>Gets how <see cref="MarkdownExtensions.MarkdigAutolinks"/> reads the URLs of the text.</summary>
    public MarkdownAutolinkOptions AutolinkOptions { get; init; } = MarkdownAutolinkOptions.Default;

    /// <summary>Gets how <see cref="MarkdownExtensions.PipeTables"/> reads and renders the pipe tables.</summary>
    public MarkdownPipeTableOptions PipeTableOptions { get; init; } = MarkdownPipeTableOptions.Default;

    /// <summary>Gets where the links of <see cref="MarkdownExtensions.JiraLinks"/> go.</summary>
    public MarkdownJiraLinkOptions JiraLinks { get; init; } = MarkdownJiraLinkOptions.Default;

    /// <summary>
    /// Gets the number of blocks that a block can be nested in, and of inlines that an inline can be nested in: a block nested
    /// in more blocks is text of a paragraph of its container, and an inline nested in more inlines is text of its container.
    /// The default is 128.
    /// </summary>
    /// <remarks>
    /// The limit keeps the tree shallow enough to be built, walked and rendered without exhausting the stack, whatever the
    /// text, such as thousands of <c>&gt;</c> or of <c>*</c>. The blocks of the content of a grid table cell are in the table
    /// and its containers, so grid tables nested in cells count too. A value greater than 256 is read as 256: deeper blocks
    /// with deeper inlines in them would overflow a stack of 1 MB, the default size of the stack of a thread on Windows.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaximumNestingDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = Math.Min(value, MaximumNestingDepthLimit);
        }
    } = 128;

    // The largest nesting depth: a document nested this deep in blocks, with inlines nested as deep in them, is parsed, rendered
    // and formatted on a stack of 1 MB with room to spare
    internal const int MaximumNestingDepthLimit = 256;

    /// <summary>Gets a value indicating whether HTML is text: no HTML block or inline HTML is recognized, and the renderer escapes it.</summary>
    /// <remarks>Autolinks, such as <c>&lt;https://example.com&gt;</c>, are still links.</remarks>
    public bool DisableHtml { get; init; }

    /// <summary>
    /// Gets the syntaxes that the parser reads in addition to CommonMark and to <see cref="Extensions"/>: inline, delimiter,
    /// fenced block and paragraph extensions. On a trigger character, the extensions are tried in order, before the built-in
    /// syntaxes, and the paragraph extensions are offered each paragraph in order.
    /// </summary>
    /// <remarks>
    /// Options with syntax extensions are equal only to their copies made with a <see langword="with"/> expression that keeps
    /// the syntax extensions.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The list contains <see langword="null"/>, an extension twice, two extensions of the same name, two delimiter extensions
    /// of the same character, or an extension of another kind.
    /// </exception>
    /// <exception cref="ArgumentNullException">The list is <see langword="null"/>.</exception>
    public IReadOnlyList<MarkdownSyntaxExtension> SyntaxExtensions
    {
        get => _syntaxExtensions?.Extensions ?? [];
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _syntaxExtensions = value.Count == 0 ? null : new Parsing.SyntaxExtensionSet([.. value], nameof(value));
        }
    }

    // The syntax extensions indexed for the parser, or null when there is none
    private Parsing.SyntaxExtensionSet? _syntaxExtensions;

    internal Parsing.SyntaxExtensionSet? SyntaxExtensionSet => _syntaxExtensions;
}
