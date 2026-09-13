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
    /// When <paramref name="comparer"/> is <see langword="null"/> or <see cref="Comparer{T}.Default"/>, a NaN floating-point value is never in range,
    /// whether it is the value or one of the bounds. This matches <c>low &lt;= actual &amp;&amp; actual &lt;= high</c>.
    /// </remarks>
    public static void InRange<T>(T actual, T low, T high, IComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (!IsInRange(actual, low, high, comparer))
            throw new AssertionException(ErrorFormatter.Format(new InRangeAssertionError<T>(actual, low, high, actualExpression, message)));
    }

    private static bool IsInRange<T>(T actual, T low, T high, IComparer<T>? comparer)
    {
        if (comparer is null || object.ReferenceEquals(comparer, Comparer<T>.Default))
        {
            // Comparer<T>.Default orders NaN below every other value and equal to itself, so it would consider NaN in [NaN, x]
            // and any value in [NaN, x]. Relational operators return false for NaN, so no range can contain it.
            if (IsNaN(actual) || IsNaN(low) || IsNaN(high))
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
