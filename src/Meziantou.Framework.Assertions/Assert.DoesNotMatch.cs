using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that the specified regular expression does not match the actual value.</summary>
    /// <param name="regex">The regular expression not expected to match <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="regexExpression">The expression that produced the regular expression.</param>
    public static void DoesNotMatch(Regex regex, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(regex))] string? regexExpression = null)
    {
        if (!regex.IsMatch(actual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotMatchAssertionError("Not expected pattern", regex.ToString(), actual, actualExpression, regexExpression, message)));
    }

    /// <summary>Asserts that the specified regular expression pattern does not match the actual value.</summary>
    /// <param name="pattern">The regular expression pattern not expected to match <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="patternExpression">The expression that produced the pattern.</param>
    /// <remarks>Like <see cref="Regex.IsMatch(string, string)"/>, the default match timeout of the application applies (see <c>REGEX_DEFAULT_MATCH_TIMEOUT</c>).</remarks>
    [SuppressMessage("Security", "MA0009:Add regex evaluation timeout", Justification = "Like Regex.IsMatch(input, pattern), the default match timeout applies, which can be configured with REGEX_DEFAULT_MATCH_TIMEOUT")]
    public static void DoesNotMatch(string pattern, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(pattern))] string? patternExpression = null)
    {
        if (!Regex.IsMatch(actual, pattern))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotMatchAssertionError("Not expected pattern", pattern, actual, actualExpression, patternExpression, message)));
    }
}
