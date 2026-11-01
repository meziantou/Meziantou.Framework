using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Regex"/>.
/// </summary>
public static class RegexExtensions
{
    /// <summary>Replaces all matches in the input string using an asynchronous replacement function.</summary>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    [SuppressMessage("Style", "IDE0220:Add explicit cast", Justification = "Not needed for older API")]
    public static async Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn)
    {
        ArgumentNullException.ThrowIfNull(regex);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(replacementFn);

        var matches = new List<(Match Match, string Replacement)>();
        foreach (Match? match in regex.Matches(input))
        {
            Debug.Assert(match is not null);

            var replacement = await replacementFn(match).ConfigureAwait(false);
            matches.Add((match, replacement));
        }

        // Right-to-left matches are returned in descending order
        if (regex.RightToLeft)
        {
            matches.Reverse();
        }

        var sb = new StringBuilder();
        var lastIndex = 0;
        foreach (var (match, replacement) in matches)
        {
            sb.Append(input, lastIndex, match.Index - lastIndex)
              .Append(replacement);

            lastIndex = match.Index + match.Length;
        }

        sb.Append(input, lastIndex, input.Length - lastIndex);
        return sb.ToString();
    }
}
