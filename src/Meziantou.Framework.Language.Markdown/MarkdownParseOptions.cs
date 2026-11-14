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
}
