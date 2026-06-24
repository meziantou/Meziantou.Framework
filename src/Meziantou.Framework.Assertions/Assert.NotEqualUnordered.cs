using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void NotEqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EqualUnordered(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<IEnumerable<T>, IEnumerable<T>>(nameof(NotEqualUnordered), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static void NotEqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EqualUnordered(expected, actual, comparer, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<IEnumerable<T>, IEnumerable<T>>(nameof(NotEqualUnordered), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqualUnordered<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EqualUnordered(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<IEnumerable<TExpected>, IEnumerable<TActual>>(nameof(NotEqualUnordered), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static Task NotEqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => EqualUnordered(expected, actual, message, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(NotEqualUnordered), "same unordered sequence", ActualExpressionText(actualExpression), message));
    }

    public static Task NotEqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => EqualUnordered(expected, actual, comparer, message, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(NotEqualUnordered), "same unordered sequence", ActualExpressionText(actualExpression), message));
    }

    [OverloadResolutionPriority(-1)]
    public static Task NotEqualUnordered<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => EqualUnordered(expected, actual, message, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(NotEqualUnordered), "same unordered sequence", ActualExpressionText(actualExpression), message));
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EqualUnordered(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>(nameof(NotEqualUnordered), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EqualUnordered(expected, actual, comparer, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>(nameof(NotEqualUnordered), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }
}
