using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Components;

public sealed class QuoteLogHighlighter : ILogHighlighter
{
    public IEnumerable<LogHighlighterResult> Process(string text)
    {
        var matches = Regex.Matches(text, @"('.*?')|("".*?"")", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        foreach (Match match in matches)
        {
            yield return new LogHighlighterResult(match.Index + 1, match.Length - 2, Priority: 0);
        }
    }
}
