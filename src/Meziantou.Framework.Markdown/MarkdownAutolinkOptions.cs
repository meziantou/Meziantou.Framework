namespace Meziantou.Framework.Markdown;

/// <summary>How <see cref="MarkdownExtensions.MarkdigAutolinks"/> reads the URLs of the text, as the options of the autolinks of Markdig.</summary>
public sealed record MarkdownAutolinkOptions
{
    /// <summary>Gets the options of Markdig.</summary>
    public static MarkdownAutolinkOptions Default { get; } = new();

    /// <summary>Gets the characters, besides whitespace, that can be before a URL: <c>*_~(</c> by default.</summary>
    public string ValidPreviousCharacters { get; init; } = "*_~(";

    /// <summary>Gets a value indicating whether the links of <c>www.</c> addresses use <c>https://</c> instead of <c>http://</c>.</summary>
    public bool UseHttpsForWwwLinks { get; init; }

    /// <summary>Gets a value indicating whether a domain can have no period, such as <c>http://localhost</c>.</summary>
    public bool AllowDomainWithoutPeriod { get; init; }

    /// <summary>Gets a value indicating whether the links have <c>target="_blank"</c>.</summary>
    public bool OpenInNewWindow { get; init; }
}
