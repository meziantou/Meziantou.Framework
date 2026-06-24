using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void NotEqual(Half expected, Half actual, Half tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, tolerance, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<Half>(expected, actual, tolerance, message, actualExpression, expectedExpression))));
    }

    public static void NotEqual(float expected, float actual, float tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, tolerance, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<float>(expected, actual, tolerance, message, actualExpression, expectedExpression))));
    }

    public static void NotEqual(double expected, double actual, double tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, tolerance, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<double>(expected, actual, tolerance, message, actualExpression, expectedExpression))));
    }

    public static void NotEqual(decimal expected, decimal actual, decimal tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, tolerance, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<decimal>(expected, actual, tolerance, message, actualExpression, expectedExpression))));
    }

    public static void NotEqual<T>(T expected, T actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<T, T>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(TExpected expected, TActual actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<TExpected, TActual>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static void NotEqual<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            Equal(expected, actual, message, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object, object>(nameof(NotEqual), "Not expected", MaterializeSpan(expected), MaterializeSpan(actual), actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(ReadOnlySpan<TExpected> expected, ReadOnlySpan<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            Equal(expected, actual, message, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object, object>(nameof(NotEqual), "Not expected", MaterializeSpan(expected), MaterializeSpan(actual), actualExpression, expectedExpression, message)));
    }

    public static void NotEqual<TExpected, TActual>(ReadOnlyMemory<TExpected> expected, ReadOnlyMemory<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<ReadOnlyMemory<TExpected>, ReadOnlyMemory<TActual>>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static void NotEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<IEnumerable<T>, IEnumerable<T>>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static void NotEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, comparer, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<IEnumerable<T>, IEnumerable<T>>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<IEnumerable<TExpected>, IEnumerable<TActual>>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static Task NotEqual<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(NotEqual), "same sequence", ActualExpressionText(actualExpression), message));
    }

    public static Task NotEqual<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => Equal(expected, actual, comparer, message, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(NotEqual), "same sequence", ActualExpressionText(actualExpression), message));
    }

    [OverloadResolutionPriority(-1)]
    public static Task NotEqual<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(NotEqual), "same sequence", ActualExpressionText(actualExpression), message));
    }

    public static void NotEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }

    public static void NotEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Equal(expected, actual, comparer, message, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message))));
    }
}
