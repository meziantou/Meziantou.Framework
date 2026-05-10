using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Components;

/// <summary>A log highlighter that detects and highlights URLs in log messages, making them clickable links.</summary>
public partial class UrlLogHighlighter : ILogHighlighter
{
    /// <summary>Processes the specified text and identifies URLs to be highlighted.</summary>
    /// <param name="text">The text to process.</param>
    /// <returns>A collection of <see cref="LogHighlighterResult"/> objects for each URL found.</returns>
    public IEnumerable<LogHighlighterResult> Process(string text)
    {
        var matches = UrlRegex().Matches(text);
        foreach (Match match in matches)
        {
            if (!TryGetHttpUrl(match.Value, out var url))
                continue;

            yield return new LogHighlighterResult(match.Index, url.Length, Priority: 0) { Link = url };
        }
    }

    private static bool TryGetHttpUrl(string value, out string url)
    {
        url = value;
        while (url.Length > 0)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsHttpScheme(uri.Scheme))
            {
                return true;
            }

            if (!ShouldTrimTrailingCharacter(url[^1]))
                break;

            url = url[..^1];
        }

        url = string.Empty;
        return false;
    }

    private static bool IsHttpScheme(string scheme)
    {
        return scheme == Uri.UriSchemeHttp || scheme == Uri.UriSchemeHttps;
    }

    private static bool ShouldTrimTrailingCharacter(char c)
    {
        return c is '.' or ',' or ';' or ':' or ')' or ']' or '}' or '!';
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 5000)]
    private static partial Regex UrlRegex();
}
