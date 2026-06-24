using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void NotEqualByStructure(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EqualByStructure(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object?, object?>(nameof(NotEqualByStructure), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }
}
