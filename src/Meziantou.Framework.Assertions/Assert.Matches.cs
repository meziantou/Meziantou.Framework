using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Asserts that the specified regular expression matches the actual value.</summary>
    /// <param name="regex">The regular expression expected to match <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="regexExpression">The expression that produced the regular expression.</param>
    public static void Matches(Regex regex, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(regex))] string? regexExpression = null)
    {
        if (regex.IsMatch(actual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new RegexMatchesAssertionError(regex.ToString(), actual, actualExpression, regexExpression)));
    }

    /// <summary>Asserts that the specified regular expression pattern matches the actual value.</summary>
    /// <param name="pattern">The regular expression pattern expected to match <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="patternExpression">The expression that produced the pattern.</param>
    public static void Matches(string pattern, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(pattern))] string? patternExpression = null)
    {
        if (Regex.IsMatch(actual, pattern, RegexOptions.None, RegexMatchTimeout))
            return;

        throw new AssertionException(ErrorFormatter.Format(new RegexMatchesAssertionError(pattern, actual, actualExpression, patternExpression)));
    }
}
