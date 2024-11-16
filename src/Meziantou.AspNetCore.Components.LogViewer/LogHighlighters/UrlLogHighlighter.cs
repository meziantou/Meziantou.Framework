using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Components;

public partial class UrlLogHighlighter : ILogHighlighter
{
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
