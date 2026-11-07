using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotInRange<T>(T actual, T low, T high, IComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (IsInRange(actual, low, high, comparer))
            throw new AssertionException(ErrorFormatter.Format(new NegativeRangeAssertionError<T>(nameof(NotInRange), actual, low, high, actualExpression, message)));
    }

    /// <summary>Asserts that a value of a <see langword="ref struct"/> type is outside the specified inclusive range, according to <see cref="IComparable{T}.CompareTo(T)"/>.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="lowExpression">The expression that produced the lower bound.</param>
    /// <param name="highExpression">The expression that produced the upper bound.</param>
    /// <remarks>
    /// This overload is used only when no other overload applies, such as for a <see langword="ref struct"/> that implements <see cref="IComparable{T}"/>.
    /// A <see langword="ref struct"/> value cannot be boxed, so the failure message shows the expressions of the bounds and the type of the values instead of the values.
    /// </remarks>
    [OverloadResolutionPriority(-3)]
    public static void NotInRange<T>(scoped in T actual, scoped in T low, scoped in T high, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(low))] string? lowExpression = null, [CallerArgumentExpression(nameof(high))] string? highExpression = null)
        where T : IComparable<T>, allows ref struct
    {
        if (!IsInRangeComparable(actual, low, high))
            return;

        throw new AssertionException(ErrorFormatter.Format(new RefStructInRangeAssertionError(isNegative: true, typeof(T), actualExpression, lowExpression, highExpression, message)));
    }
}
