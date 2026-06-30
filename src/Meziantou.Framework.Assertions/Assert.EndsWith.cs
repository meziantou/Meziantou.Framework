using System.Diagnostics.CodeAnalysis;
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
    public static void EndsWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (!actual.IsEmpty && comparer.Equals(expected, actual[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ValueEndsWithAssertionError<T>(expected, actual, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an enumerable ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        actualSnapshot.EnsureComplete();

        if (actualSnapshot.Items.Count > 0 && comparer.Equals(expected, actualSnapshot.Items[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionEndsWithAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a non-generic enumerable ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        actualSnapshot.EnsureComplete();

        if (actualSnapshot.Items.Count > 0 && object.Equals(expected, actualSnapshot.Items[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionEndsWithAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a span ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanEndsWithAssertionError<T>(expected, actual, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a character span ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (actual.EndsWith(expected, comparison))
            return;

        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparison);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharEndsWithAssertionError(expected, actual, firstDifferenceIndex, comparison, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a string ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(EndsWith), "Expected suffix", expected, comparison, actualExpression, expectedExpression, message)));
        }

        if (actual.EndsWith(expected, comparison))
            return;

        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparison);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharEndsWithAssertionError(expected, actual, firstDifferenceIndex, comparison, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task EndsWith<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        expectedSnapshot.EnsureComplete();
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionEndsWithAssertionError<T, T>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)).ConfigureAwait(false));
    }

    /// <summary>Asserts that a non-generic enumerable ends with the specified non-generic suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEndsWithAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)));
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
