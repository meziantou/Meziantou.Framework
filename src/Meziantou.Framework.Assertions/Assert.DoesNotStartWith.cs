using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void DoesNotStartWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            StartsWith(expected, actual, comparer, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<T, object>(nameof(DoesNotStartWith), "Not expected prefix", expected, MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith<T>(T expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => StartsWith(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<T, IEnumerable<T>>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotStartWith(object? expected, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => StartsWith(expected, actual, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object?, System.Collections.IEnumerable>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotStartWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            StartsWith(expected, actual, comparer, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object, object>(nameof(DoesNotStartWith), "Not expected prefix", MaterializeSpan(expected), MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            StartsWith(expected, actual, comparison, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<string, object>(nameof(DoesNotStartWith), "Not expected prefix", expected.ToString(), MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith(string expected, string actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => StartsWith(expected, actual, comparison, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<string, string>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static Task DoesNotStartWith<T>(IEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => StartsWith(expected, actual, comparer, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(DoesNotStartWith), "matching prefix", ActualExpressionText(actualExpression)));
    }

    public static void DoesNotStartWith(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
            SucceedWhenAssertionFails(() => StartsWith(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null))));
    }
}
