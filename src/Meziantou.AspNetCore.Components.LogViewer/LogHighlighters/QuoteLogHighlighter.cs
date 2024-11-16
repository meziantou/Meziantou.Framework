using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Components;

public sealed partial class QuoteLogHighlighter : ILogHighlighter
{
    public IEnumerable<LogHighlighterResult> Process(string text)
    {
        var matches = QuoteRegex().Matches(text);
        foreach (Match match in matches)
        {
            yield return new LogHighlighterResult(match.Index + 1, match.Length - 2, Priority: 0);
        }
    }

    [GeneratedRegex(@"('.*?')|("".*?"")", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex QuoteRegex();
}
