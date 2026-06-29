using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEqualByStructure(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var failure = GetStructuralDifference(expected, actual, "$", new HashSet<StructuralReferencePair>(), StructuralComparisonOptions.Create(options: null));
        if (failure is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualByStructureAssertionError(expected, actual, actualExpression, expectedExpression, message)));
    }
}
