using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotSame(object? expected, object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!object.ReferenceEquals(expected, actual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeSameAssertionError(expected, actual, actualExpression, expectedExpression)));
    }
}
