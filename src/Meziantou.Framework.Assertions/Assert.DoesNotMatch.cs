using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    /// <summary>
    /// Asserts that the specified regular expression does not match the actual value.
    /// </summary>
    /// <param name="regex">The regular expression not expected to match <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="regexExpression">The expression that produced the regular expression.</param>
    public static void DoesNotMatch(Regex regex, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(regex))] string? regexExpression = null)
    {
        if (!regex.IsMatch(actual))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotMatchAssertionError("Not expected pattern", regex.ToString(), actual, actualExpression, regexExpression, message: null)));
    }

    /// <summary>
    /// Asserts that the specified regular expression pattern does not match the actual value.
    /// </summary>
    /// <param name="pattern">The regular expression pattern not expected to match <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="patternExpression">The expression that produced the pattern.</param>
    public static void DoesNotMatch(string pattern, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(pattern))] string? patternExpression = null)
    {
        if (!Regex.IsMatch(actual, pattern, RegexOptions.None, RegexMatchTimeout))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotMatchAssertionError("Not expected pattern", pattern, actual, actualExpression, patternExpression, message: null)));
    }
}
