using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void NotProperSubset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => ProperSubset(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeSetAssertionError(expected, actual, isSuperset: false, actualExpression, expectedExpression))));
    }

    public static void NotProperSubset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => ProperSubset(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeSetAssertionError(expected, actual, isSuperset: false, actualExpression, expectedExpression))));
    }

    public static void NotProperSuperset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => ProperSuperset(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeSetAssertionError(expected, actual, isSuperset: true, actualExpression, expectedExpression))));
    }

    public static void NotProperSuperset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => ProperSuperset(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeSetAssertionError(expected, actual, isSuperset: true, actualExpression, expectedExpression))));
    }
}
