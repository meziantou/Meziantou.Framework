namespace Meziantou.Framework.Language.Markdown;

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

    /// <summary>Gets the shortcodes and the smileys that <see cref="MarkdownExtensions.Emoji"/> reads: by default, the ones of Markdig.</summary>
    public MarkdownEmojiMapping EmojiMapping { get; init; } = MarkdownEmojiMapping.Default;

    /// <summary>Gets a value indicating whether a block quote in a container, such as a list item, can be an alert. By default, only the block quotes of the document are.</summary>
    public bool AllowNestedAlerts { get; init; }

    /// <summary>Gets a value indicating whether HTML is text: no HTML block or inline HTML is recognized, and the renderer escapes it.</summary>
    /// <remarks>Autolinks, such as <c>&lt;https://example.com&gt;</c>, are still links.</remarks>
    public bool DisableHtml { get; init; }
}
