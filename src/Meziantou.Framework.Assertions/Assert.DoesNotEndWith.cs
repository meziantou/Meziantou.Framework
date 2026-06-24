using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void DoesNotEndWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            EndsWith(expected, actual, comparer, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<T, object>(nameof(DoesNotEndWith), "Not expected suffix", expected, MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith<T>(T expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EndsWith(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<T, IEnumerable<T>>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotEndWith(object? expected, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EndsWith(expected, actual, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object?, System.Collections.IEnumerable>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotEndWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            EndsWith(expected, actual, comparer, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object, object>(nameof(DoesNotEndWith), "Not expected suffix", MaterializeSpan(expected), MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            EndsWith(expected, actual, comparison, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<string, object>(nameof(DoesNotEndWith), "Not expected suffix", expected.ToString(), MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith(string expected, string actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => EndsWith(expected, actual, comparison, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<string, string>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static Task DoesNotEndWith<T>(IEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => EndsWith(expected, actual, comparer, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(DoesNotEndWith), "matching suffix", ActualExpressionText(actualExpression)));
    }

    public static void DoesNotEndWith(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
            SucceedWhenAssertionFails(() => EndsWith(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null))));
    }
}
