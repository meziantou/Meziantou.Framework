using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void DoesNotContain<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            Contains(expected, actual, comparer, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<T, object>(nameof(DoesNotContain), "Not expected item", expected, MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotContain<T>(T expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Contains(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<T, IEnumerable<T>>(nameof(DoesNotContain), "Not expected item", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotContain<TKey, TValue>(TKey expected, IEnumerable<KeyValuePair<TKey, TValue>> actual, IEqualityComparer<TKey>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Contains(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<TKey, IEnumerable<KeyValuePair<TKey, TValue>>>(nameof(DoesNotContain), "Not expected key", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotContain<TKey, TValue>(TKey expected, Dictionary<TKey, TValue> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        where TKey : notnull
    {
        SucceedWhenAssertionFails(() => Contains(expected, actual, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<TKey, Dictionary<TKey, TValue>>(nameof(DoesNotContain), "Not expected key", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotContain(object? expected, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Contains(expected, actual, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object?, System.Collections.IEnumerable>(nameof(DoesNotContain), "Not expected", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotContain(object? expected, System.Collections.IDictionary actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Contains(expected, actual, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object?, System.Collections.IDictionary>(nameof(DoesNotContain), "Not expected key", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static void DoesNotContain(string expected, System.Collections.IDictionary actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotContain((object?)expected, actual, actualExpression, expectedExpression);
    }

    public static void DoesNotContain<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            Contains(expected, actual, comparer, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<object, object>(nameof(DoesNotContain), "Not expected", MaterializeSpan(expected), MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotContain(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        try
        {
            Contains(expected, actual, comparison, actualExpression, expectedExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<string, object>(nameof(DoesNotContain), "Not expected", expected.ToString(), MaterializeSpan(actual), actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotContain(string expected, string actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        SucceedWhenAssertionFails(() => Contains(expected, actual, comparison, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<string, string>(nameof(DoesNotContain), "Not expected", expected, actual, actualExpression, expectedExpression, message: null))));
    }

    public static Task DoesNotContain<T>(IEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => Contains(expected, actual, comparer, actualExpression, expectedExpression), () => CreateNegativeTextAssertion(nameof(DoesNotContain), "contained item", ActualExpressionText(actualExpression)));
    }

    public static void DoesNotContain(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
            SucceedWhenAssertionFails(() => Contains(expected, actual, comparer, actualExpression, expectedExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeValueAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>(nameof(DoesNotContain), "Not expected", expected, actual, actualExpression, expectedExpression, message: null))));
    }
}
