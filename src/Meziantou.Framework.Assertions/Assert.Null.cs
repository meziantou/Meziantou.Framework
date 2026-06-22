using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    /// <summary>
    /// Asserts that an object is null.
    /// </summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Null(object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NullAssertionError(actual, actualExpression)));
    }
}
