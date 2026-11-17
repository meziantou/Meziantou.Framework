using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Markdown;

/// <summary>Where the links of <see cref="MarkdownExtensions.JiraLinks"/> go: <c>{BaseUrl}/{BasePath}/{issue}</c>.</summary>
public sealed record MarkdownJiraLinkOptions
{
    /// <summary>Gets options without a base URL: the links are <c>/browse/PROJ-123</c>.</summary>
    public static MarkdownJiraLinkOptions Default { get; } = new();

    /// <summary>Gets the URL of the Jira site, such as <c>https://example.atlassian.net</c>.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The URL can be empty or relative, and is written as it is")]
    public string BaseUrl { get; init; } = "";

    /// <summary>Gets the path of the issues on the site, <c>/browse</c> by default.</summary>
    public string BasePath { get; init; } = "/browse";

    /// <summary>Gets a value indicating whether the links have <c>target="_blank"</c>, which is the default.</summary>
    public bool OpenInNewWindow { get; init; } = true;

    /// <summary>Gets the destination of the link to an issue, such as <c>PROJ-123</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="issue"/> is <see langword="null"/>.</exception>
    public string GetDestination(string issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return BaseUrl.TrimEnd('/') + "/" + BasePath.Trim('/') + "/" + issue;
    }
}
