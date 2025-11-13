using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Components;

/// <summary>A log highlighter that detects and highlights quoted strings (both single and double quotes) in log messages.</summary>
public sealed partial class QuoteLogHighlighter : ILogHighlighter
{
    /// <summary>Processes the specified text and identifies quoted strings to be highlighted.</summary>
    /// <param name="text">The text to process.</param>
    /// <returns>A collection of <see cref="LogHighlighterResult"/> objects for each quoted string found.</returns>
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
