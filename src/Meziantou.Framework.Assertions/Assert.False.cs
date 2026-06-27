using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>
    /// Asserts that a condition is false. If the condition is true, an <see cref="AssertionException"/> is thrown.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="expression">The expression that caused the failure.</param>
    public static void False([DoesNotReturnIf(true)] bool condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (condition)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new FalseAssertionError(condition, message, expression)));
        }
    }

    /// <summary>
    /// Asserts that a nullable condition is false. If the condition is true or null, an <see cref="AssertionException"/> is thrown.
    /// </summary>
    /// <param name="condition">The nullable condition to evaluate.</param>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="expression">The expression that caused the failure.</param>
    public static void False(bool? condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (condition is not false)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new FalseAssertionError(condition, message, expression)));
        }
    }
}
