using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Components;

public class UrlLogHighlighter : ILogHighlighter
{
    public IEnumerable<LogHighlighterResult> Process(string text)
    {
        var matches = Regex.Matches(text, @"(http|https)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        foreach (Match match in matches)
        {
            yield return new LogHighlighterResult(match.Index, match.Length, Priority: 0) { Link = match.Value };
        }
    }
}
