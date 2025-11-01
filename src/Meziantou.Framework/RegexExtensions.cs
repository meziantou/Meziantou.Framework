using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Regex"/>.
/// </summary>
/// <example>
/// <code>
/// var regex = new Regex(@"\d+");
/// string result = await regex.ReplaceAsync("abc 123 def 456", async match =>
/// {
///     await Task.Delay(10);
///     return (int.Parse(match.Value) * 2).ToString();
/// });
/// // result: "abc 246 def 912"
/// </code>
/// </example>
public static class RegexExtensions
{
    /// <summary>Replaces all matches in the input string using an asynchronous replacement function.</summary>
    [SuppressMessage("Style", "IDE0220:Add explicit cast", Justification = "Not needed for older API")]
    public static async Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn)
    {
        var sb = new StringBuilder();
        var lastIndex = 0;

        foreach (Match? match in regex.Matches(input))
        {
            Debug.Assert(match is not null);

            var replacement = await replacementFn(match).ConfigureAwait(false);
            sb.Append(input, lastIndex, match.Index - lastIndex)
              .Append(replacement);

            lastIndex = match.Index + match.Length;
        }

        sb.Append(input, lastIndex, input.Length - lastIndex);
        return sb.ToString();
    }
}
