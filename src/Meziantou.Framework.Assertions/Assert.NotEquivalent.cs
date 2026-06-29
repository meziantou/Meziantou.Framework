using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEquivalent(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotEquivalent(expected, actual, options: null, message, actualExpression, expectedExpression);
    }

    public static void NotEquivalent(object? expected, object? actual, EquivalentOptions? options, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var failure = GetStructuralDifference(expected, actual, "$", new HashSet<StructuralReferencePair>(), StructuralComparisonOptions.Create(options));
        if (failure is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEquivalentAssertionError(expected, actual, actualExpression, expectedExpression, message)));
    }
}
