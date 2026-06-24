using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void NotInRange<T>(T actual, T low, T high, IComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => InRange(actual, low, high, comparer, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeRangeAssertionError<T>(nameof(NotInRange), actual, low, high, actualExpression))));
    }
}
