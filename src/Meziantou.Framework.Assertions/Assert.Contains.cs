using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    /// <summary>
    /// Asserts that a span contains the specified value.
    /// </summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < actual.Length; i++)
        {
            if (comparer.Equals(expected, actual[i]))
                return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new ValueContainsAssertionError<T>(expected, actual, actualExpression, expectedExpression)));
    }

    /// <summary>
    /// Asserts that an enumerable contains the specified value.
    /// </summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains<T>(T expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = new CollectionSnapshot<T>(actual);

        foreach (var item in actualSnapshot)
        {
            if (comparer.Equals(expected, item))
                return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new ValueCollectionContainsAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>
    /// Asserts that a non-generic enumerable contains the specified value.
    /// </summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(object? expected, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));

        foreach (var item in actualSnapshot)
        {
            if (object.Equals(expected, item))
                return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new ValueCollectionContainsAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>
    /// Asserts that a span contains the specified subsequence.
    /// </summary>
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

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanContainsAssertionError<T>(expected, actual, actualExpression, expectedExpression)));
    }

    /// <summary>
    /// Asserts that a character span contains the specified substring.
    /// </summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparison">The comparison used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.Contains(expected, comparison))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>
    /// Asserts that a string contains the specified substring.
    /// </summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparison">The comparison used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(string expected, string actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.Contains(expected, comparison))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>
    /// Asserts that an asynchronous sequence contains the specified contiguous subsequence.
    /// </summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task Contains<T>(IEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);

        EnsureComplete(expectedSnapshot);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        if (ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new CollectionAsyncCollectionContainsAssertionError<T, T>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    /// <summary>
    /// Asserts that a non-generic enumerable contains the specified contiguous non-generic subsequence.
    /// </summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));

        EnsureComplete(expectedSnapshot);
        EnsureComplete(actualSnapshot);
        if (ContainsSubsequence(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionContainsAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression)));
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
        using var enumerator = snapshot.GetEnumerator();
        while (enumerator.MoveNext())
        {
        }
    }

    private static async Task EnsureCompleteAsync<T>(AsyncCollectionSnapshot<T> snapshot)
    {
        await using var enumerator = snapshot.GetAsyncEnumerator();
        while (await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
        }
    }
}
