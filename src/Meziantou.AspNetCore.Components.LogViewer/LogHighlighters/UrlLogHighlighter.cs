using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Components;

/// <summary>
/// A log highlighter that detects and highlights URLs in log messages, making them clickable links.
/// </summary>
public partial class UrlLogHighlighter : ILogHighlighter
{
    /// <summary>
    /// Processes the specified text and identifies URLs to be highlighted.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <returns>A collection of <see cref="LogHighlighterResult"/> objects for each URL found.</returns>
    public IEnumerable<LogHighlighterResult> Process(string text)
    {
        var matches = UrlRegex().Matches(text);
        foreach (Match match in matches)
        {
            yield return new LogHighlighterResult(match.Index, match.Length, Priority: 0) { Link = match.Value };
        }
    }

    [GeneratedRegex(@"(http|https)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex UrlRegex();
}
