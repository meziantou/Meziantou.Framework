using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    /// <summary>
    /// Asserts that two objects refer to the same instance.
    /// </summary>
    /// <param name="expected">The expected object instance.</param>
    /// <param name="actual">The actual object instance.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Same(object? expected, object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (object.ReferenceEquals(expected, actual))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new SameAssertionError(expected, actual, actualExpression, expectedExpression)));
    }
}
