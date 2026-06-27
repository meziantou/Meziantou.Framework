using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEqualByStructure(object? expected, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var failure = GetStructuralDifference(expected, actual, "$", new HashSet<StructuralReferencePair>());
        if (failure is not null)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualByStructureAssertionError(expected, actual, actualExpression, expectedExpression, message)));
    }
}
