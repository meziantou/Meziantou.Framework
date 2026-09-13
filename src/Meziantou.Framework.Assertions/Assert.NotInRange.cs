using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotInRange<T>(T actual, T low, T high, IComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (IsInRange(actual, low, high, comparer))
            throw new AssertionException(ErrorFormatter.Format(new NegativeRangeAssertionError<T>(nameof(NotInRange), actual, low, high, actualExpression, message)));
    }
}
