using System.Text.RegularExpressions;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Regex"/>.
/// </summary>
public static class RegexExtensions
{
    /// <summary>Replaces all matches in the input string using an asynchronous replacement function.</summary>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    public static Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn)
    {
        return ReplaceAsync(regex, input, replacementFn, count: -1, startat: GetDefaultStartAt(regex, input), CancellationToken.None);
    }

    /// <summary>Replaces all matches in the input string using an asynchronous replacement function.</summary>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    public static Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn, CancellationToken cancellationToken)
    {
        return ReplaceAsync(regex, input, replacementFn, count: -1, startat: GetDefaultStartAt(regex, input), cancellationToken);
    }

    /// <summary>Replaces at most <paramref name="count"/> matches in the input string using an asynchronous replacement function.</summary>
    /// <param name="regex">The regular expression.</param>
    /// <param name="input">The string to search for a match.</param>
    /// <param name="replacementFn">The function that computes the replacement of each match.</param>
    /// <param name="count">The maximum number of times the replacement can occur, or -1 to replace all the matches.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    public static Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn, int count, CancellationToken cancellationToken = default)
    {
        return ReplaceAsync(regex, input, replacementFn, count, startat: GetDefaultStartAt(regex, input), cancellationToken);
    }

    /// <summary>Replaces at most <paramref name="count"/> matches, searched from <paramref name="startat"/>, in the input string using an asynchronous replacement function.</summary>
    /// <param name="regex">The regular expression.</param>
    /// <param name="input">The string to search for a match.</param>
    /// <param name="replacementFn">The function that computes the replacement of each match.</param>
    /// <param name="count">The maximum number of times the replacement can occur, or -1 to replace all the matches.</param>
    /// <param name="startat">The character position in the input string where the search begins, as for <see cref="Regex.Replace(string, MatchEvaluator, int, int)"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    public static Task<string> ReplaceAsync(this Regex regex, string input, Func<Match, Task<string>> replacementFn, int count, int startat, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacementFn);

        return ReplaceAsync(regex, input, (match, _) => new ValueTask<string>(replacementFn(match)), count, startat, cancellationToken).AsTask();
    }

    /// <summary>Replaces all matches in the input string using an asynchronous replacement function.</summary>
    /// <param name="regex">The regular expression.</param>
    /// <param name="input">The string to search for a match.</param>
    /// <param name="replacementFn">The function that computes the replacement of each match. It receives <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    public static ValueTask<string> ReplaceAsync(this Regex regex, string input, Func<Match, CancellationToken, ValueTask<string>> replacementFn, CancellationToken cancellationToken = default)
    {
        return ReplaceAsync(regex, input, replacementFn, count: -1, startat: GetDefaultStartAt(regex, input), cancellationToken);
    }

    /// <summary>Replaces at most <paramref name="count"/> matches in the input string using an asynchronous replacement function.</summary>
    /// <param name="regex">The regular expression.</param>
    /// <param name="input">The string to search for a match.</param>
    /// <param name="replacementFn">The function that computes the replacement of each match. It receives <paramref name="cancellationToken"/>.</param>
    /// <param name="count">The maximum number of times the replacement can occur, or -1 to replace all the matches.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    public static ValueTask<string> ReplaceAsync(this Regex regex, string input, Func<Match, CancellationToken, ValueTask<string>> replacementFn, int count, CancellationToken cancellationToken = default)
    {
        return ReplaceAsync(regex, input, replacementFn, count, startat: GetDefaultStartAt(regex, input), cancellationToken);
    }

    /// <summary>Replaces at most <paramref name="count"/> matches, searched from <paramref name="startat"/>, in the input string using an asynchronous replacement function.</summary>
    /// <param name="regex">The regular expression.</param>
    /// <param name="input">The string to search for a match.</param>
    /// <param name="replacementFn">The function that computes the replacement of each match. It receives <paramref name="cancellationToken"/>.</param>
    /// <param name="count">The maximum number of times the replacement can occur, or -1 to replace all the matches.</param>
    /// <param name="startat">The character position in the input string where the search begins, as for <see cref="Regex.Replace(string, MatchEvaluator, int, int)"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>The replacement function is called for each match, in the order the matches are found: from right to left when the regex uses <see cref="RegexOptions.RightToLeft"/>.</remarks>
    public static ValueTask<string> ReplaceAsync(this Regex regex, string input, Func<Match, CancellationToken, ValueTask<string>> replacementFn, int count, int startat, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regex);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(replacementFn);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, -1);
        ArgumentOutOfRangeException.ThrowIfNegative(startat);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startat, input.Length);

        return ReplaceCoreAsync(regex, input, replacementFn, count, startat, cancellationToken);
    }

    private static async ValueTask<string> ReplaceCoreAsync(Regex regex, string input, Func<Match, CancellationToken, ValueTask<string>> replacementFn, int count, int startat, CancellationToken cancellationToken)
    {
        if (count is 0)
            return input;

        var matches = new List<(Match Match, string Replacement)>();
        var match = regex.Match(input, startat);
        while (match.Success)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var replacement = await replacementFn(match, cancellationToken).ConfigureAwait(false);
            matches.Add((match, replacement));
            if (matches.Count == count)
                break;

            match = match.NextMatch();
        }

        if (matches.Count is 0)
            return input;

        // Right-to-left matches are returned in descending order
        if (regex.RightToLeft)
        {
            matches.Reverse();
        }

        var sb = new StringBuilder();
        var lastIndex = 0;
        foreach (var (m, replacement) in matches)
        {
            sb.Append(input, lastIndex, m.Index - lastIndex)
              .Append(replacement);

            lastIndex = m.Index + m.Length;
        }

        sb.Append(input, lastIndex, input.Length - lastIndex);
        return sb.ToString();
    }

    // Regex.Replace starts at the end of the input for a right-to-left regex
    private static int GetDefaultStartAt(Regex regex, string input)
    {
        ArgumentNullException.ThrowIfNull(regex);
        ArgumentNullException.ThrowIfNull(input);

        return regex.RightToLeft ? input.Length : 0;
    }
}
