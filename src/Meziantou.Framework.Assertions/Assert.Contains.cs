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
    public static void Contains<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < actual.Length; i++)
        {
            if (comparer.Equals(expected, actual[i]))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new ValueContainsAssertionError<T>(expected, actual, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The collection to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void Contains<T>(T expected, [NotNull] ICollection<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<T>("Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        if (actual.Contains(expected))
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void Contains<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<T>("Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        for (var i = 0; actualSnapshot.TryGetItem(i, out var item); i++)
        {
            if (comparer.Equals(expected, item))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains at least one item that matches the specified predicate.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate used to select matching items.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    [OverloadResolutionPriority(1)]
    public static void Contains<T>([NotNull] IEnumerable<T>? actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsPredicateNullActualAssertionError(actualExpression, predicateExpression, message)));
        }

        using var matchingSnapshot = CollectionSnapshot.Create<T>(EnumerateMatchingItems(actual, predicate));
        if (matchingSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionContainsPredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression, message)));
    }

    /// <summary>Asserts that a dictionary-like collection contains the specified key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary-like collection to inspect.</param>
    /// <param name="comparer">The comparer used to compare keys when <paramref name="actual"/> is not a dictionary.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    [OverloadResolutionPriority(1)]
    public static TValue Contains<TKey, TValue>(TKey expected, [NotNull] IEnumerable<KeyValuePair<TKey, TValue>>? actual, IEqualityComparer<TKey>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<TKey>("Expected key expression", "Expected key", expected, actualExpression, expectedExpression, message)));
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

        throw new AssertionException(ErrorFormatter.Format(new KeyValuePairCollectionContainsAssertionError<TKey, TValue>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a dictionary contains the specified key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    [OverloadResolutionPriority(1)]
    public static TValue Contains<TKey, TValue>(TKey expected, [NotNull] Dictionary<TKey, TValue>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        where TKey : notnull
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<TKey>("Expected key expression", "Expected key", expected, actualExpression, expectedExpression, message)));
        }

        if (actual.TryGetValue(expected, out var value))
            return value;

        using var actualSnapshot = CollectionSnapshot.Create<KeyValuePair<TKey, TValue>>(actual);
        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new KeyValuePairCollectionContainsAssertionError<TKey, TValue>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a non-generic enumerable contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(-1)]
    public static void Contains(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<object?>("Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);

        for (var i = 0; actualSnapshot.TryGetItem(i, out var item); i++)
        {
            if (object.Equals(expected, item))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a non-generic dictionary contains the specified key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    [OverloadResolutionPriority(-1)]
    public static object? Contains(object? expected, [NotNull] System.Collections.IDictionary? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<object?>("Expected key expression", "Expected key", expected, actualExpression, expectedExpression, message)));
        }

        if (actual.Contains(expected!))
            return actual[expected!];

        throw new AssertionException(ErrorFormatter.Format(new DictionaryContainsAssertionError(expected, actual, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a non-generic dictionary contains the specified string key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    public static object? Contains(string expected, [NotNull] System.Collections.IDictionary? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        return Contains((object?)expected, actual, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a span contains the specified subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (ContainsSubsequence(expected, actual, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanContainsAssertionError<T>(expected, actual, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a character span contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (actual.Contains(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparison, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a string contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringContainsNullActualAssertionError(expected, comparison, actualExpression, expectedExpression, message)));
        }

        if (actual.Contains(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparison, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence contains the specified contiguous subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task Contains<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<IEnumerable<T>>("Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        expectedSnapshot.EnsureComplete();
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionContainsAssertionError<T, T>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression, message)).ConfigureAwait(false));
    }

    /// <summary>Asserts that a non-generic enumerable contains the specified contiguous non-generic subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ContainsNullActualAssertionError<System.Collections.IEnumerable>("Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        if (ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionContainsAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression, message)));
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
}
