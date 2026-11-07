using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a value is inside the specified inclusive range.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    /// <param name="comparer">The comparer used to order values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <remarks>
    /// When <paramref name="comparer"/> is <see langword="null"/> or <see cref="Comparer{T}.Default"/>, a NaN floating-point value or a null <see cref="Nullable{T}"/>
    /// is never in range, whether it is the value or one of the bounds. This matches <c>low &lt;= actual &amp;&amp; actual &lt;= high</c>.
    /// </remarks>
    public static void InRange<T>(T actual, T low, T high, IComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (!IsInRange(actual, low, high, comparer))
            throw new AssertionException(ErrorFormatter.Format(new InRangeAssertionError<T>(actual, low, high, actualExpression, message)));
    }

    /// <summary>Asserts that a value of a <see langword="ref struct"/> type is inside the specified inclusive range, according to <see cref="IComparable{T}.CompareTo(T)"/>.</summary>
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
    public static void InRange<T>(scoped in T actual, scoped in T low, scoped in T high, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(low))] string? lowExpression = null, [CallerArgumentExpression(nameof(high))] string? highExpression = null)
        where T : IComparable<T>, allows ref struct
    {
        if (IsInRangeComparable(actual, low, high))
            return;

        throw new AssertionException(ErrorFormatter.Format(new RefStructInRangeAssertionError(isNegative: false, typeof(T), actualExpression, lowExpression, highExpression, message)));
    }

    private static bool IsInRangeComparable<T>(scoped in T actual, scoped in T low, scoped in T high)
        where T : IComparable<T>, allows ref struct
    {
        return actual.CompareTo(low) >= 0 && actual.CompareTo(high) <= 0;
    }

    private static bool IsInRange<T>(T actual, T low, T high, IComparer<T>? comparer)
    {
        if (comparer is null || object.ReferenceEquals(comparer, Comparer<T>.Default))
        {
            // Comparer<T>.Default orders NaN below every other value and equal to itself, so it would consider NaN in [NaN, x]
            // and any value in [NaN, x]. Relational operators return false for NaN, so no range can contain it.
            if (IsNaN(actual) || IsNaN(low) || IsNaN(high))
                return false;

            // Comparer<T>.Default orders null below every other value of a Nullable<T>, while the lifted relational
            // operators return false when an operand is null, so no range contains null and a null bound contains nothing.
            // For a value type T, "is null" is only true for a Nullable<T> without a value.
            if (typeof(T).IsValueType && (actual is null || low is null || high is null))
                return false;

            comparer = Comparer<T>.Default;
        }

        return comparer.Compare(actual, low) >= 0 && comparer.Compare(actual, high) <= 0;
    }

    private static bool IsNaN<T>(T value)
    {
        return value switch
        {
            double d => double.IsNaN(d),
            float f => float.IsNaN(f),
            Half h => Half.IsNaN(h),
            NFloat n => NFloat.IsNaN(n),
            _ => false,
        };
    }
}
