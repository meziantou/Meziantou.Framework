using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// The overloads and their priorities mirror Contains (see Assert.Contains.cs), so a call binds the same way for all these assertions.
public partial class Assert
{
    /// <summary>Asserts that a span starts with the specified value.</summary>
    /// <param name="expected">The value expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void StartsWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[0]))
        {
            throw new AssertionException(ErrorFormatter.Format(new ValueStartsWithAssertionError<T>(expected, actual, actualExpression, expectedExpression, message)));
        }
    }

    /// <summary>Asserts that an array starts with the specified value.</summary>
    /// <param name="expected">The value expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    // An array would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void StartsWith<T>(T expected, [NotNull] T[]? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        StartsWith(expected, new ReadOnlySpan<T>(actual), comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a string starts with the specified character.</summary>
    /// <param name="expected">The character expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparer">The comparer used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    // A string would otherwise bind to the ReadOnlySpan<char> overload, which turns a null string into an empty span.
    [OverloadResolutionPriority(1)]
    public static void StartsWith(char expected, [NotNull] string? actual, IEqualityComparer<char>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<char>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        StartsWith(expected, actual.AsSpan(), comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that an enumerable starts with the specified value.</summary>
    /// <param name="expected">The value expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void StartsWith<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (!actualSnapshot.TryGetItem(0, out var item) || !comparer.Equals(expected, item))
        {
            throw new AssertionException(ErrorFormatter.Format(new ValueCollectionStartsWithAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
        }
    }

    /// <summary>Asserts that a non-generic enumerable starts with the specified value.</summary>
    /// <param name="expected">The value expected at the start of <paramref name="actual"/>. A string compared to a sequence of characters is compared as a prefix.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(-1)]
    public static void StartsWith(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        // A string is never equal to a char, so comparing it to the first item of a char sequence could never succeed.
        switch (expected, actual)
        {
            case (string expectedString, string actualString):
                StartsWith(expectedString, actualString, StringComparison.Ordinal, message, actualExpression, expectedExpression);
                return;

            case (string expectedString, IEnumerable<char>):
                StartsWith((System.Collections.IEnumerable)expectedString, actual, comparer: null, message, actualExpression, expectedExpression);
                return;
        }

        StartsWithValue(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    private static void StartsWithValue(object? expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);

        if (!actualSnapshot.TryGetItem(0, out var item) || !Equals(expected, item, comparer))
        {
            throw new AssertionException(ErrorFormatter.Format(new ValueCollectionStartsWithAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
        }
    }

    /// <summary>Asserts that a span starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var firstDifferenceIndex = GetFirstDifferenceIndex(expected, actual, comparer);
        if (firstDifferenceIndex is not null)
        {
            throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanStartsWithAssertionError<T>(expected, actual, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)));
        }
    }

    /// <summary>Asserts that an enumerable starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>. When <paramref name="expected"/> is itself the first item of <paramref name="actual"/>, the assertion also succeeds.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void StartsWith<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        var firstDifferenceIndex = GetFirstPrefixDifferenceIndex(expected, expectedSnapshot, actualSnapshot, comparer);
        if (firstDifferenceIndex is not null)
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionStartsWithAssertionError<T, T>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)));
        }
    }

    /// <summary>Asserts that a character span starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        StartsWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a character span starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparisonType">The comparison used to compare <paramref name="expected"/> with the start of <paramref name="actual"/>.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.StartsWith(expected, comparisonType))
            return;

        var firstDifferenceIndex = GetFirstDifferenceIndex(expected, actual, comparisonType);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharStartsWithAssertionError(expected, actual, firstDifferenceIndex, comparisonType, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a string starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void StartsWith(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        StartsWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a string starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparisonType">The comparison used to compare <paramref name="expected"/> with the start of <paramref name="actual"/>.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void StartsWith(string expected, [NotNull] string? actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(StartsWith), "Expected prefix", expected, comparisonType, actualExpression, expectedExpression, message)));
        }

        if (actual.StartsWith(expected, comparisonType))
            return;

        var firstDifferenceIndex = GetFirstDifferenceIndex(expected, actual, comparisonType);
        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharStartsWithAssertionError(expected, actual, firstDifferenceIndex, comparisonType, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence starts with the specified prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>. When <paramref name="expected"/> is itself the first item of <paramref name="actual"/>, the assertion also succeeds.</param>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task StartsWith<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        var firstDifferenceIndex = await GetFirstPrefixDifferenceIndexAsync(expected, expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false);
        if (firstDifferenceIndex is not null)
        {
            throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionStartsWithAssertionError<T, T>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)).ConfigureAwait(false));
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

    /// <summary>
    /// Returns the index of the first item of <paramref name="expected"/> that does not match <paramref name="actual"/>,
    /// or <see langword="null"/> when <paramref name="actual"/> starts with <paramref name="expected"/>, or when
    /// <paramref name="expected"/> is itself the first item of <paramref name="actual"/>.
    /// </summary>
    private static int? GetFirstPrefixDifferenceIndex<T>(IEnumerable<T> expected, CollectionSnapshot<T> expectedSnapshot, CollectionSnapshot<T> actual, IEqualityComparer<T> comparer)
    {
        for (var index = 0; expectedSnapshot.TryGetItem(index, out var expectedItem); index++)
        {
            if (!actual.TryGetItem(index, out var actualItem) || !comparer.Equals(expectedItem, actualItem))
            {
                if (expected is T expectedAsItem && actual.TryGetItem(0, out var firstItem) && comparer.Equals(expectedAsItem, firstItem))
                    return null;

                return index;
            }
        }

        return null;
    }

    private static async Task<int?> GetFirstPrefixDifferenceIndexAsync<T>(IEnumerable<T> expected, CollectionSnapshot<T> expectedSnapshot, AsyncCollectionSnapshot<T> actual, IEqualityComparer<T> comparer)
    {
        for (var index = 0; expectedSnapshot.TryGetItem(index, out var expectedItem); index++)
        {
            var (actualHasNext, actualItem) = await actual.TryGetItem(index).ConfigureAwait(false);
            if (!actualHasNext || !comparer.Equals(expectedItem, actualItem))
            {
                if (expected is T expectedAsItem && await actual.TryGetItem(0).ConfigureAwait(false) is (true, var firstItem) && comparer.Equals(expectedAsItem, firstItem))
                    return null;

                return index;
            }
        }

        return null;
    }

    /// <inheritdoc cref="GetFirstPrefixDifferenceIndex{T}(IEnumerable{T}, CollectionSnapshot{T}, CollectionSnapshot{T}, IEqualityComparer{T})"/>
    /// <remarks>
    /// A string or a custom comparer disables the item match: a string is never equal to a char, and a non-generic
    /// comparer may not accept a sequence as an argument.
    /// </remarks>
    private static int? GetFirstPrefixDifferenceIndex(System.Collections.IEnumerable expected, CollectionSnapshot<object?> expectedSnapshot, CollectionSnapshot<object?> actual, System.Collections.IEqualityComparer? comparer)
    {
        for (var index = 0; expectedSnapshot.TryGetItem(index, out var expectedItem); index++)
        {
            if (!actual.TryGetItem(index, out var actualItem) || !Equals(expectedItem, actualItem, comparer))
            {
                if (comparer is null && expected is not string && actual.TryGetItem(0, out var firstItem) && object.Equals(expected, firstItem))
                    return null;

                return index;
            }
        }

        return null;
    }

    /// <summary>Asserts that a non-generic enumerable starts with the specified non-generic prefix.</summary>
    /// <param name="expected">The prefix expected at the start of <paramref name="actual"/>. Without <paramref name="comparer"/>, the assertion also succeeds when <paramref name="expected"/> is itself the first item of <paramref name="actual"/>. A string is compared as an item, unless <paramref name="actual"/> is a sequence of characters.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void StartsWith(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(StartsWith), "Expected expression", "Expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        // A string is itself an IEnumerable, so without this guard it binds here rather than to the object overload
        // and is compared as a char prefix of a collection whose elements are not chars. That comparison can only
        // match an empty string, which makes the assertion fail for a matching first item and pass for "".
        if (expected is string && actual is not IEnumerable<char>)
        {
            StartsWithValue(expected, actual, comparer, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        var firstDifferenceIndex = GetFirstPrefixDifferenceIndex(expected, expectedSnapshot, actualSnapshot, comparer);
        if (firstDifferenceIndex is not null)
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionStartsWithAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, firstDifferenceIndex.GetValueOrDefault(), actualExpression, expectedExpression, message)));
        }
    }

    private static StringComparison GetOrdinalComparison(bool ignoreCase)
    {
        return ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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
