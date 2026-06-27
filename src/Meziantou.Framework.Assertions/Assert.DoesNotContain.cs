using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotContain<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < actual.Length; i++)
        {
            if (!comparer.Equals(expected, actual[i]))
                continue;

            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanExpectedActualValueAssertionError<T, T>(nameof(DoesNotContain), "Not expected item", expected, actual, actualExpression, expectedExpression, message: null)));
        }
    }

    public static void DoesNotContain<T>(T expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        foreach (var item in actual)
        {
            if (!comparer.Equals(expected, item))
                continue;

            throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotContainAssertionError<T, IEnumerable<T>>("Not expected item", expected, actual, actualExpression, expectedExpression, message: null)));
        }
    }

    public static void DoesNotContain<T>(IEnumerable<T> actual, Func<T, bool> predicate, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        using var matchingSnapshot = new CollectionSnapshot<T>(EnumerateMatchingItems(actual, predicate));
        using var matchingEnumerator = matchingSnapshot.GetEnumerator();
        if (!matchingEnumerator.MoveNext())
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionDoesNotContainPredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression)));
    }

    public static void DoesNotContain<TKey, TValue>(TKey expected, IEnumerable<KeyValuePair<TKey, TValue>> actual, IEqualityComparer<TKey>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<TKey>.Default;
        foreach (var item in actual)
        {
            if (!comparer.Equals(expected, item.Key))
                continue;

            throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotContainAssertionError<TKey, IEnumerable<KeyValuePair<TKey, TValue>>>("Not expected key", expected, actual, actualExpression, expectedExpression, message: null)));
        }
    }

    public static void DoesNotContain<TKey, TValue>(TKey expected, Dictionary<TKey, TValue> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        where TKey : notnull
    {
        if (!actual.ContainsKey(expected))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotContainAssertionError<TKey, Dictionary<TKey, TValue>>("Not expected key", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotContain(object? expected, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        foreach (var item in actual)
        {
            if (!object.Equals(expected, item))
                continue;

            throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotContainAssertionError<object?, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message: null)));
        }
    }

    public static void DoesNotContain(object? expected, System.Collections.IDictionary actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.Contains(expected!))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotContainAssertionError<object?, System.Collections.IDictionary>("Not expected key", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotContain(string expected, System.Collections.IDictionary actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotContain((object?)expected, actual, actualExpression, expectedExpression);
    }

    public static void DoesNotContain<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (!ContainsSubsequence(expected, actual, comparer))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(DoesNotContain), "Not expected", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotContain(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.Contains(expected, comparison))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanValueAssertionError<char, char>(nameof(DoesNotContain), "Not expected", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotContain(string expected, string actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.Contains(expected, comparison))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotContainAssertionError<string, string>("Not expected", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static async Task DoesNotContain<T>(IEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        EnsureComplete(expectedSnapshot);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        if (!ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeExpressionAssertionError(nameof(DoesNotContain), "contained item", AssertionFormatter.FormatExpression(actualExpression), message: null)));
    }

    public static void DoesNotContain(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));
        EnsureComplete(expectedSnapshot);
        EnsureComplete(actualSnapshot);
        if (!ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotContainAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message: null)));
    }
}
