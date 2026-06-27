using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a value is inside the specified inclusive range.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    /// <param name="comparer">The comparer used to order values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void InRange<T>(T actual, T low, T high, IComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= Comparer<T>.Default;
        if (comparer.Compare(actual, low) >= 0 && comparer.Compare(actual, high) <= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new InRangeAssertionError<T>(actual, low, high, actualExpression)));
    }
}
