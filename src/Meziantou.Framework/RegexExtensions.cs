using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Meziantou.Framework;

public static class RegexExtensions
{
    public static async Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn)
    {
        var sb = new StringBuilder();
        var lastIndex = 0;

        foreach (Match? match in regex.Matches(input))
        {
            Debug.Assert(match != null);

            var replacement = await replacementFn(match).ConfigureAwait(false);
            sb.Append(input, lastIndex, match.Index - lastIndex)
              .Append(replacement);

            lastIndex = match.Index + match.Length;
        }

        sb.Append(input, lastIndex, input.Length - lastIndex);
        return sb.ToString();
    }
}
