using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a span ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (!actual.IsEmpty && comparer.Equals(expected, actual[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ValueEndsWithAssertionError<T>(expected, actual, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that an enumerable ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith<T>(T expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        EnsureComplete(actualSnapshot);

        if (actualSnapshot.Items.Count > 0 && comparer.Equals(expected, actualSnapshot.Items[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionEndsWithAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a non-generic enumerable ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(object? expected, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        EnsureComplete(actualSnapshot);

        if (actualSnapshot.Items.Count > 0 && object.Equals(expected, actualSnapshot.Items[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionEndsWithAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a span ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanEndsWithAssertionError<T>(expected, actual, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a character span ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparison">The comparison used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.EndsWith(expected, comparison))
            return;

        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparison);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharEndsWithAssertionError(expected, actual, firstDifferenceIndex, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a string ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparison">The comparison used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(string expected, string actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.EndsWith(expected, comparison))
            return;

        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparison);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharEndsWithAssertionError(expected, actual, firstDifferenceIndex, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that an asynchronous sequence ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task EndsWith<T>(IEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);

        EnsureComplete(expectedSnapshot);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionEndsWithAssertionError<T, T>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    /// <summary>Asserts that a non-generic enumerable ends with the specified non-generic suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));

        EnsureComplete(expectedSnapshot);
        EnsureComplete(actualSnapshot);
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEndsWithAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression)));
    }

    private static int? GetFirstSuffixDifferenceIndex<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T> comparer)
    {
        if (expected.Length > actual.Length)
            return actual.Length;

        var actualOffset = actual.Length - expected.Length;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!comparer.Equals(expected[i], actual[actualOffset + i]))
                return i;
        }

        return null;
    }

    private static int GetFirstSuffixDifferenceIndex(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison)
    {
        if (expected.Length > actual.Length)
            return actual.Length;

        var actualOffset = actual.Length - expected.Length;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!actual.Slice(actualOffset + i, 1).Equals(expected.Slice(i, 1), comparison))
                return i;
        }

        return expected.Length;
    }

    private static int? GetFirstSuffixDifferenceIndex<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T> comparer)
    {
        if (expected.Count > actual.Count)
            return actual.Count;

        var actualOffset = actual.Count - expected.Count;
        for (var i = 0; i < expected.Count; i++)
        {
            if (!comparer.Equals(expected[i], actual[actualOffset + i]))
                return i;
        }

        return null;
    }

    private static int? GetFirstSuffixDifferenceIndex(IReadOnlyList<object?> expected, IReadOnlyList<object?> actual, System.Collections.IEqualityComparer? comparer)
    {
        if (expected.Count > actual.Count)
            return actual.Count;

        var actualOffset = actual.Count - expected.Count;
        for (var i = 0; i < expected.Count; i++)
        {
            if (!Equals(expected[i], actual[actualOffset + i], comparer))
                return i;
        }

        return null;
    }
}
