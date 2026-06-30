using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a span contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void Contains<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < actual.Length; i++)
        {
            if (comparer.Equals(expected, actual[i]))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new ValueContainsAssertionError<T>(expected, actual, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that an enumerable contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void Contains<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<T>("Expected expression", "Expected item", expected, actualExpression, expectedExpression)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        for (var i = 0; actualSnapshot.TryGetItem(i, out var item); i++)
        {
            if (comparer.Equals(expected, item))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that an enumerable contains at least one item that matches the specified predicate.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate used to select matching items.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    [OverloadResolutionPriority(1)]
    public static void Contains<T>([NotNull] IEnumerable<T>? actual, Func<T, bool> predicate, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsPredicateNullActualAssertionError(actualExpression, predicateExpression)));
        }

        using var matchingSnapshot = CollectionSnapshot.Create<T>(EnumerateMatchingItems(actual, predicate));
        if (matchingSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionContainsPredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression)));
    }

    /// <summary>Asserts that a dictionary-like collection contains the specified key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary-like collection to inspect.</param>
    /// <param name="comparer">The comparer used to compare keys when <paramref name="actual"/> is not a dictionary.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    [OverloadResolutionPriority(1)]
    public static TValue Contains<TKey, TValue>(TKey expected, [NotNull] IEnumerable<KeyValuePair<TKey, TValue>>? actual, IEqualityComparer<TKey>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<TKey>("Expected key expression", "Expected key", expected, actualExpression, expectedExpression)));
        }

        if (actual is IReadOnlyDictionary<TKey, TValue> readOnlyDictionary && readOnlyDictionary.TryGetValue(expected, out var readOnlyValue))
            return readOnlyValue;

        if (actual is IDictionary<TKey, TValue> dictionary && dictionary.TryGetValue(expected, out var value))
            return value;

        comparer ??= EqualityComparer<TKey>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<KeyValuePair<TKey, TValue>>(actual);
        for (var i = 0; actualSnapshot.TryGetItem(i, out var item); i++)
        {
            if (comparer.Equals(expected, item.Key))
                return item.Value;
        }

        throw new AssertionException(ErrorFormatter.Format(new KeyValuePairCollectionContainsAssertionError<TKey, TValue>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a dictionary contains the specified key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    [OverloadResolutionPriority(1)]
    public static TValue Contains<TKey, TValue>(TKey expected, [NotNull] Dictionary<TKey, TValue>? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        where TKey : notnull
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<TKey>("Expected key expression", "Expected key", expected, actualExpression, expectedExpression)));
        }

        if (actual.TryGetValue(expected, out var value))
            return value;

        using var actualSnapshot = CollectionSnapshot.Create<KeyValuePair<TKey, TValue>>(actual);
        EnsureComplete(actualSnapshot);
        throw new AssertionException(ErrorFormatter.Format(new KeyValuePairCollectionContainsAssertionError<TKey, TValue>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a non-generic enumerable contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(-1)]
    public static void Contains(object? expected, [NotNull] System.Collections.IEnumerable? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<object?>("Expected expression", "Expected item", expected, actualExpression, expectedExpression)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);

        for (var i = 0; actualSnapshot.TryGetItem(i, out var item); i++)
        {
            if (object.Equals(expected, item))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a non-generic dictionary contains the specified key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    [OverloadResolutionPriority(-1)]
    public static object? Contains(object? expected, [NotNull] System.Collections.IDictionary? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<object?>("Expected key expression", "Expected key", expected, actualExpression, expectedExpression)));
        }

        if (actual.Contains(expected!))
            return actual[expected!];

        throw new AssertionException(ErrorFormatter.Format(new DictionaryContainsAssertionError(expected, actual, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a non-generic dictionary contains the specified string key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    public static object? Contains(string expected, [NotNull] System.Collections.IDictionary? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return Contains((object?)expected, actual, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a span contains the specified subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (ContainsSubsequence(expected, actual, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanContainsAssertionError<T>(expected, actual, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a character span contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparison">The comparison used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.Contains(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a string contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparison">The comparison used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(string expected, [NotNull] string? actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringContainsNullActualAssertionError(expected, comparison, actualExpression, expectedExpression)));
        }

        if (actual.Contains(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that an asynchronous sequence contains the specified contiguous subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task Contains<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<IEnumerable<T>>("Expected expression", "Expected", expected, actualExpression, expectedExpression)));
        }

        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        EnsureComplete(expectedSnapshot);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        if (ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionContainsAssertionError<T, T>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    /// <summary>Asserts that a non-generic enumerable contains the specified contiguous non-generic subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<System.Collections.IEnumerable>("Expected expression", "Expected", expected, actualExpression, expectedExpression)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        EnsureComplete(expectedSnapshot);
        EnsureComplete(actualSnapshot);
        if (ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionContainsAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression)));
    }

    private static bool ContainsSubsequence<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T> comparer)
    {
        if (expected.IsEmpty)
            return true;

        if (expected.Length > actual.Length)
            return false;

        for (var start = 0; start <= actual.Length - expected.Length; start++)
        {
            var matches = true;
            for (var i = 0; i < expected.Length; i++)
            {
                if (!comparer.Equals(expected[i], actual[start + i]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static bool ContainsSubsequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T> comparer)
    {
        if (expected.Count == 0)
            return true;

        if (expected.Count > actual.Count)
            return false;

        for (var start = 0; start <= actual.Count - expected.Count; start++)
        {
            var matches = true;
            for (var i = 0; i < expected.Count; i++)
            {
                if (!comparer.Equals(expected[i], actual[start + i]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static bool ContainsSubsequence(IReadOnlyList<object?> expected, IReadOnlyList<object?> actual, System.Collections.IEqualityComparer? comparer)
    {
        if (expected.Count == 0)
            return true;

        if (expected.Count > actual.Count)
            return false;

        for (var start = 0; start <= actual.Count - expected.Count; start++)
        {
            var matches = true;
            for (var i = 0; i < expected.Count; i++)
            {
                if (!Equals(expected[i], actual[start + i], comparer))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static void EnsureComplete<T>(CollectionSnapshot<T> snapshot)
    {
        for (var i = 0; snapshot.TryGetItem(i, out _); i++)
        {
        }
    }

    private static async Task EnsureCompleteAsync<T>(AsyncCollectionSnapshot<T> snapshot)
    {
        for (var i = 0; await snapshot.TryGetItem(i).ConfigureAwait(false) is (true, _); i++)
        {
        }
    }
}
