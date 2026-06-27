using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>
    /// Asserts that a condition is true. If the condition is false, an <see cref="AssertionException"/> is thrown.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="expression">The expression that caused the failure.</param>
    public static void True([DoesNotReturnIf(false)] bool condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
        {
            throw new AssertionException(ErrorFormatter.Format(new TrueAssertionError(condition, message, expression)));
        }
    }

    /// <summary>
    /// Asserts that a nullable condition is true. If the condition is false or null, an <see cref="AssertionException"/> is thrown.
    /// </summary>
    /// <param name="condition">The nullable condition to evaluate.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="expression">The expression that caused the failure.</param>
    public static void True(bool? condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (condition is not true)
        {
            throw new AssertionException(ErrorFormatter.Format(new TrueAssertionError(condition, message, expression)));
        }
    }
}
