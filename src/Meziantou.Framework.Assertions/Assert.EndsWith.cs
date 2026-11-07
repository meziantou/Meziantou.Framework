using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// The overloads and their priorities mirror Contains (see Assert.Contains.cs), so a call binds the same way for all these assertions.
public partial class Assert
{
    /// <summary>Asserts that a span ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void EndsWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (!actual.IsEmpty && comparer.Equals(expected, actual[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ValueEndsWithAssertionError<T>(expected, actual, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an array ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    // An array would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void EndsWith<T>(T expected, [NotNull] T[]? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        EndsWith(expected, new ReadOnlySpan<T>(actual), comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a string ends with the specified character.</summary>
    /// <param name="expected">The character expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparer">The comparer used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    // A string would otherwise bind to the ReadOnlySpan<char> overload, which turns a null string into an empty span.
    [OverloadResolutionPriority(1)]
    public static void EndsWith(char expected, [NotNull] string? actual, IEqualityComparer<char>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<char>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        EndsWith(expected, actual.AsSpan(), comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that an enumerable ends with the specified value.</summary>
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
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
    /// <param name="expected">The value expected at the end of <paramref name="actual"/>. A string compared to a sequence of characters is compared as a suffix.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(-1)]
    public static void EndsWith(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        // A string is never equal to a char, so comparing it to the last item of a char sequence could never succeed.
        switch (expected, actual)
        {
            case (string expectedString, string actualString):
                EndsWith(expectedString, actualString, StringComparison.Ordinal, message, actualExpression, expectedExpression);
                return;

            case (string expectedString, IEnumerable<char>):
                EndsWith((System.Collections.IEnumerable)expectedString, actual, comparer: null, message, actualExpression, expectedExpression);
                return;
        }

        EndsWithValue(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    private static void EndsWithValue(object? expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        actualSnapshot.EnsureComplete();

        if (actualSnapshot.Items.Count > 0 && Equals(expected, actualSnapshot.Items[^1], comparer))
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

    /// <summary>Asserts that an enumerable ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>. When <paramref name="expected"/> is itself the last item of <paramref name="actual"/>, the assertion also succeeds.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void EndsWith<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(EndsWith), "Expected expression", "Expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, expectedSnapshot.Items, actualSnapshot.Items, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEndsWithAssertionError<T, T>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a character span ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EndsWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a character span ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparisonType">The comparison used to compare <paramref name="expected"/> with the end of <paramref name="actual"/>.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void EndsWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.EndsWith(expected, comparisonType))
            return;

        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparisonType);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharEndsWithAssertionError(expected, actual, firstDifferenceIndex, comparisonType, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a string ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void EndsWith(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EndsWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a string ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparisonType">The comparison used to compare <paramref name="expected"/> with the end of <paramref name="actual"/>.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void EndsWith(string expected, [NotNull] string? actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(EndsWith), "Expected suffix", expected, comparisonType, actualExpression, expectedExpression, message)));
        }

        if (actual.EndsWith(expected, comparisonType))
            return;

        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, actual, comparisonType);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharEndsWithAssertionError(expected, actual, firstDifferenceIndex, comparisonType, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence ends with the specified suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>. When <paramref name="expected"/> is itself the last item of <paramref name="actual"/>, the assertion also succeeds.</param>
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
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, expectedSnapshot.Items, actualSnapshot.Items, comparer);
        if (firstDifferenceIndex is null)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionEndsWithAssertionError<T, T>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)).ConfigureAwait(false));
    }

    /// <summary>Asserts that a non-generic enumerable ends with the specified non-generic suffix.</summary>
    /// <param name="expected">The suffix expected at the end of <paramref name="actual"/>. Without <paramref name="comparer"/>, the assertion also succeeds when <paramref name="expected"/> is itself the last item of <paramref name="actual"/>. A string is compared as an item, unless <paramref name="actual"/> is a sequence of characters.</param>
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

        // A string is itself an IEnumerable, so without this guard it binds here rather than to the object overload
        // and is compared as a char suffix of a collection whose elements are not chars. That comparison can only
        // match an empty string, which makes the assertion fail for a matching last item and pass for "".
        if (expected is string && actual is not IEnumerable<char>)
        {
            EndsWithValue(expected, actual, comparer, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expected, expectedSnapshot.Items, actualSnapshot.Items, comparer);
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

    /// <summary>
    /// Returns the index of the first item of <paramref name="expectedItems"/> that does not match the end of
    /// <paramref name="actual"/>, or <see langword="null"/> when <paramref name="actual"/> ends with it, or when
    /// <paramref name="expected"/> is itself the last item of <paramref name="actual"/>.
    /// </summary>
    private static int? GetFirstSuffixDifferenceIndex<T>(IEnumerable<T> expected, IReadOnlyList<T> expectedItems, IReadOnlyList<T> actual, IEqualityComparer<T> comparer)
    {
        var firstDifferenceIndex = GetFirstSuffixDifferenceIndex(expectedItems, actual, comparer);
        if (firstDifferenceIndex is not null && expected is T expectedAsItem && actual.Count > 0 && comparer.Equals(expectedAsItem, actual[^1]))
            return null;

        return firstDifferenceIndex;
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

    /// <inheritdoc cref="GetFirstSuffixDifferenceIndex{T}(IEnumerable{T}, IReadOnlyList{T}, IReadOnlyList{T}, IEqualityComparer{T})"/>
    /// <remarks>
    /// A string or a custom comparer disables the item match: a string is never equal to a char, and a non-generic
    /// comparer may not accept a sequence as an argument.
    /// </remarks>
    private static int? GetFirstSuffixDifferenceIndex(System.Collections.IEnumerable expected, IReadOnlyList<object?> expectedItems, IReadOnlyList<object?> actual, System.Collections.IEqualityComparer? comparer)
    {
        int? firstDifferenceIndex = null;
        if (expectedItems.Count > actual.Count)
        {
            firstDifferenceIndex = actual.Count;
        }
        else
        {
            var actualOffset = actual.Count - expectedItems.Count;
            for (var i = 0; i < expectedItems.Count; i++)
            {
                if (!Equals(expectedItems[i], actual[actualOffset + i], comparer))
                {
                    firstDifferenceIndex = i;
                    break;
                }
            }
        }

        if (firstDifferenceIndex is not null && comparer is null && expected is not string && actual.Count > 0 && object.Equals(expected, actual[^1]))
            return null;

        return firstDifferenceIndex;
    }
}
