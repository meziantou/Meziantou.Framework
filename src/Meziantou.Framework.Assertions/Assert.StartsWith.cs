using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a span starts with the specified value.</summary>
    /// <param name="expected">The value expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[0]))
        {
            throw new AssertionException(ErrorFormatter.Format(new ValueStartsWithAssertionError<T>(expected, actual, actualExpression, expectedExpression)));
        }
    }

    /// <summary>Asserts that an enumerable starts with the specified value.</summary>
    /// <param name="expected">The value expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext() || !comparer.Equals(expected, actualEnumerator.Current))
        {
            throw new AssertionException(ErrorFormatter.Format(new ValueCollectionStartsWithAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression)));
        }
    }

    /// <summary>Asserts that a non-generic enumerable starts with the specified value.</summary>
    /// <param name="expected">The value expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith(object? expected, [NotNull] System.Collections.IEnumerable? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression)));
        }

        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext() || !object.Equals(expected, actualEnumerator.Current))
        {
            throw new AssertionException(ErrorFormatter.Format(new ValueCollectionStartsWithAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression)));
        }
    }

    /// <summary>Asserts that a span starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var firstDifferenceIndex = GetFirstDifferenceIndex(expected, actual, comparer);
        if (firstDifferenceIndex is not null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanStartsWithAssertionError<T>(expected, actual, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression)));
        }
    }

    /// <summary>Asserts that a character span starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparison">The comparison used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.StartsWith(expected, comparison))
            return;

        var firstDifferenceIndex = GetFirstDifferenceIndex(expected, actual, comparison);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharStartsWithAssertionError(expected, actual, firstDifferenceIndex, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a string starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith(string expected, [NotNull] string? actual, bool ignoreCase = false, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(StartsWith), "Expected prefix", expected, comparison, actualExpression, expectedExpression)));
        }

        if (actual.StartsWith(expected, comparison))
            return;

        var firstDifferenceIndex = GetFirstDifferenceIndex(expected, actual, comparison);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharStartsWithAssertionError(expected, actual, firstDifferenceIndex, comparison, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that an asynchronous sequence starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task StartsWith<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression)));
        }

        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        var index = 0;
        while (expectedEnumerator.MoveNext())
        {
            var actualHasNext = await actualEnumerator.MoveNextAsync().ConfigureAwait(false);
            if (!actualHasNext || !comparer.Equals(expectedEnumerator.Current, actualEnumerator.Current))
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionStartsWithAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            index++;
        }
    }

    private static int? GetFirstDifferenceIndex<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T> comparer)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (i >= actual.Length || !comparer.Equals(expected[i], actual[i]))
                return i;
        }

        return null;
    }

    private static int GetFirstDifferenceIndex(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (i >= actual.Length || !actual.Slice(i, 1).Equals(expected.Slice(i, 1), comparison))
                return i;
        }

        return expected.Length;
    }

    /// <summary>Asserts that a non-generic enumerable starts with the specified non-generic prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression)));
        }

        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        var index = 0;
        while (expectedEnumerator.MoveNext())
        {
            var actualHasNext = actualEnumerator.MoveNext();
            if (!actualHasNext || !Equals(expectedEnumerator.Current, actualEnumerator.Current, comparer))
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionStartsWithAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, index, actualExpression, expectedExpression)));
            }

            index++;
        }
    }

    private static bool Equals(object? expected, object? actual, System.Collections.IEqualityComparer? comparer)
    {
        if (comparer is not null)
            return comparer.Equals(expected, actual);

        return object.Equals(expected, actual);
    }

    private static IEnumerable<object?> EnumerateObjects(System.Collections.IEnumerable value)
    {
        foreach (var item in value)
        {
            yield return item;
        }
    }
}
