using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
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
            throw new AssertionException(AssertionFormatter.Default.Format(new FalseAssertionError(message, expression)));
        }
    }
}
