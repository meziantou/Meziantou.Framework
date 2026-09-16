using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// Overload resolution priorities:
// - 2: string overloads and generic subsequence overloads. A sequence whose element type matches the element type of
//      actual is searched as a subsequence, even when it could also be an item (List<object>). With two strings, the
//      string overloads are better conversions than the IEnumerable<char> subsequence overload.
// - 1: item, key and predicate overloads. T[] and string overloads keep a null array or string from becoming an empty span.
// - -1: non-generic item overloads, used only when nothing else applies. A positional message after two strings binds
//      here (a message-only string overload would make the MA0001 analyzer ask for a StringComparison on such calls),
//      so two strings are routed to the ordinal substring check at runtime.
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

    /// <summary>Asserts that an array contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    // An array would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void Contains<T>(T expected, [NotNull] T[]? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(Contains), "Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        Contains(expected, new ReadOnlySpan<T>(actual), comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a string contains the specified character.</summary>
    /// <param name="expected">The character expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparer">The comparer used to compare characters.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    // A string would otherwise bind to the ReadOnlySpan<char> overload, which turns a null string into an empty span.
    [OverloadResolutionPriority(1)]
    public static void Contains(char expected, [NotNull] string? actual, IEqualityComparer<char>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<char>(nameof(Contains), "Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        Contains(expected, actual.AsSpan(), comparer, message, actualExpression, expectedExpression);
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
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(Contains), "Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        if (actual.Contains(expected))
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<T>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="actual"/> is an <see cref="ICollection{T}"/>, the collection decides (like <see cref="Enumerable.Contains{TSource}(IEnumerable{TSource}, TSource)"/>), so a set uses its own comparer.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(1)]
    public static void Contains<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(Contains), "Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        if (comparer is null && actual is ICollection<T> collection)
        {
            if (collection.Contains(expected))
                return;

            using var collectionSnapshot = CollectionSnapshot.Create<T>(actual);
            throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<T>(expected, collectionSnapshot, actualExpression, expectedExpression, message)));
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
            throw new AssertionException(ErrorFormatter.Format(new PredicateNullActualAssertionError(nameof(Contains), actualExpression, predicateExpression, message)));
        }

        using var matchingSnapshot = CollectionSnapshot.Create<T>(EnumerateMatchingItems(actual, predicate));
        if (matchingSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionContainsPredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression, message)));
    }

    /// <summary>Asserts that a dictionary-like collection contains the specified key and returns the associated value.</summary>
    /// <param name="expected">The key expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The dictionary-like collection to inspect.</param>
    /// <param name="comparer">The comparer used to compare keys. When <see langword="null"/> and <paramref name="actual"/> is a dictionary, only the dictionary's own lookup decides.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected key.</param>
    [OverloadResolutionPriority(1)]
    public static TValue Contains<TKey, TValue>(TKey expected, [NotNull] IEnumerable<KeyValuePair<TKey, TValue>>? actual, IEqualityComparer<TKey>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<TKey>(nameof(Contains), "Expected key expression", "Expected key", expected, actualExpression, expectedExpression, message)));
        }

        using var actualSnapshot = CollectionSnapshot.Create<KeyValuePair<TKey, TValue>>(actual);
        if (TryFindKey(expected, actual, actualSnapshot, comparer, out var value, out _))
            return value;

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
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<TKey>(nameof(Contains), "Expected key expression", "Expected key", expected, actualExpression, expectedExpression, message)));
        }

        // A dictionary cannot contain a null key, and looking one up throws.
        if (expected is not null && actual.TryGetValue(expected, out var value))
            return value;

        using var actualSnapshot = CollectionSnapshot.Create<KeyValuePair<TKey, TValue>>(actual);
        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new KeyValuePairCollectionContainsAssertionError<TKey, TValue>(expected!, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a non-generic enumerable contains the specified value.</summary>
    /// <param name="expected">The value expected in <paramref name="actual"/>. A string searched in a sequence of characters is searched as a substring.</param>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(-1)]
    public static void Contains(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(Contains), "Expected expression", "Expected item", expected, actualExpression, expectedExpression, message)));
        }

        // A string is never equal to a char, so comparing it to each item of a char sequence could never succeed.
        switch (expected, actual)
        {
            case (string expectedString, string actualString):
                Contains(expectedString, actualString, StringComparison.Ordinal, message, actualExpression, expectedExpression);
                return;

            case (string expectedString, IEnumerable<char>):
                Contains((System.Collections.IEnumerable)expectedString, actual, comparer: null, message, actualExpression, expectedExpression);
                return;
        }

        ContainsValue(expected, actual, comparer: null, message, actualExpression, expectedExpression);
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
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(Contains), "Expected key expression", "Expected key", expected, actualExpression, expectedExpression, message)));
        }

        // A dictionary cannot contain a null key, and looking one up throws.
        if (expected is not null && actual.Contains(expected))
            return actual[expected];

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
        if (IndexOfSubsequence(expected, actual, comparer) >= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanContainsAssertionError<T>(expected, actual, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains the specified contiguous subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>. When <paramref name="expected"/> is itself an item of <paramref name="actual"/>, the assertion also succeeds.</param>
    /// <param name="actual">The enumerable to inspect. It is read only until the subsequence is found.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void Contains<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(Contains), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (IndexOfSubsequenceOrItem(expected, expectedSnapshot, actualSnapshot, comparer) >= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionContainsAssertionError<T, T>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a character span contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        Contains(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a character span contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparisonType">The comparison used to search for <paramref name="expected"/>.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual.Contains(expected, comparisonType))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparisonType, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a string contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the comparison ignores casing (OrdinalIgnoreCase); otherwise, it is case-sensitive (Ordinal).</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void Contains(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        Contains(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a string contains the specified substring.</summary>
    /// <param name="expected">The substring expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="comparisonType">The comparison used to search for <paramref name="expected"/>.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    [OverloadResolutionPriority(2)]
    public static void Contains(string expected, [NotNull] string? actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(Contains), "Expected", expected, comparisonType, actualExpression, expectedExpression, message)));
        }

        if (actual.Contains(expected, comparisonType))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCharContainsAssertionError(expected, actual, comparisonType, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence contains the specified contiguous subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>. When <paramref name="expected"/> is itself an item of <paramref name="actual"/>, the assertion also succeeds.</param>
    /// <param name="actual">The sequence to inspect. It is read only until the subsequence is found.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static async Task Contains<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(Contains), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        if (await IndexOfSubsequenceOrItemAsync(expected, expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false) >= 0)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new CollectionAsyncCollectionContainsAssertionError<T, T>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression, message)).ConfigureAwait(false));
    }

    /// <summary>Asserts that a non-generic enumerable contains the specified contiguous non-generic subsequence.</summary>
    /// <param name="expected">The subsequence expected in <paramref name="actual"/>. Without <paramref name="comparer"/>, the assertion also succeeds when <paramref name="expected"/> is itself an item of <paramref name="actual"/>. A string is searched as an item, unless <paramref name="actual"/> is a sequence of characters.</param>
    /// <param name="actual">The enumerable to inspect. It is read only until the subsequence is found.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void Contains(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(Contains), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        // A string is itself an IEnumerable, so without this guard it binds here rather than to the object overload
        // and is searched as a char subsequence of a collection whose elements are not chars. That comparison can
        // only match an empty string, which makes the assertion fail for a present item and pass for "".
        if (expected is string && actual is not IEnumerable<char>)
        {
            ContainsValue(expected, actual, comparer, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        if (IndexOfSubsequenceOrItem(expected, expectedSnapshot, actualSnapshot, comparer) >= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionContainsAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    private static void ContainsValue(object? expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);

        for (var i = 0; actualSnapshot.TryGetItem(i, out var item); i++)
        {
            if (Equals(expected, item, comparer))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new ValueCollectionContainsAssertionError<object?>(expected, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    /// <summary>Looks the key up the same way for Contains and DoesNotContain, so the two assertions are exact complements.</summary>
    private static bool TryFindKey<TKey, TValue>(TKey expected, IEnumerable<KeyValuePair<TKey, TValue>> actual, CollectionSnapshot<KeyValuePair<TKey, TValue>> actualSnapshot, IEqualityComparer<TKey>? comparer, out TValue value, out int? index)
    {
        // A dictionary compares keys with its own comparer, and it cannot contain a null key (looking one up throws).
        // Without an explicit comparer, its answer is final: scanning the pairs with the default comparer would find
        // keys that a stricter dictionary comparer (e.g. ReferenceEqualityComparer) does not consider equal.
        var isDictionary = actual is IReadOnlyDictionary<TKey, TValue> or IDictionary<TKey, TValue>;
        if (isDictionary)
        {
            if (expected is not null)
            {
                if (actual is IReadOnlyDictionary<TKey, TValue> readOnlyDictionary && readOnlyDictionary.TryGetValue(expected, out var readOnlyValue))
                {
                    value = readOnlyValue;
                    index = null;
                    return true;
                }

                if (actual is IDictionary<TKey, TValue> dictionary && dictionary.TryGetValue(expected, out var dictionaryValue))
                {
                    value = dictionaryValue;
                    index = null;
                    return true;
                }
            }

            if (comparer is null)
            {
                value = default!;
                index = null;
                return false;
            }
        }

        comparer ??= EqualityComparer<TKey>.Default;
        for (var i = 0; actualSnapshot.TryGetItem(i, out var item); i++)
        {
            if (comparer.Equals(expected, item.Key))
            {
                value = item.Value;
                index = i;
                return true;
            }
        }

        value = default!;
        index = null;
        return false;
    }

    private static int IndexOfSubsequence<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T> comparer)
    {
        if (expected.IsEmpty)
            return 0;

        for (var start = 0; start <= actual.Length - expected.Length; start++)
        {
            if (SubsequenceMatchesAt(expected, actual, start, comparer))
                return start;
        }

        return -1;
    }

    private static bool SubsequenceMatchesAt<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, int start, IEqualityComparer<T> comparer)
    {
        for (var i = 0; i < expected.Length; i++)
        {
            if (!comparer.Equals(expected[i], actual[start + i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the index of the first position of <paramref name="actual"/> where <paramref name="expected"/> starts as a
    /// contiguous subsequence, or where <paramref name="expected"/> is itself an item. <paramref name="actual"/> is read
    /// only up to the match, so an endless sequence that contains the subsequence can be searched.
    /// </summary>
    private static int IndexOfSubsequenceOrItem<T>(IEnumerable<T> expected, CollectionSnapshot<T> expectedSnapshot, CollectionSnapshot<T> actual, IEqualityComparer<T> comparer)
    {
        expectedSnapshot.EnsureComplete();
        var expectedItems = expectedSnapshot.Items;
        if (expectedItems.Count == 0)
            return 0;

        var isItem = expected is T;
        var expectedItem = isItem ? (T)(object)expected : default;
        var canMatchSubsequence = true;
        for (var start = 0; actual.TryGetItem(start, out var item); start++)
        {
            if (isItem && comparer.Equals(expectedItem, item))
                return start;

            if (!canMatchSubsequence)
                continue;

            // The last item of a match at this position is read first: when it does not exist, no later position can match
            if (!actual.TryGetItem(start + expectedItems.Count - 1, out _))
            {
                if (!isItem)
                    return -1;

                canMatchSubsequence = false;
                continue;
            }

            if (SubsequenceMatchesAt(expectedItems, actual.Items, start, comparer))
                return start;
        }

        return -1;
    }

    private static async Task<int> IndexOfSubsequenceOrItemAsync<T>(IEnumerable<T> expected, CollectionSnapshot<T> expectedSnapshot, AsyncCollectionSnapshot<T> actual, IEqualityComparer<T> comparer)
    {
        expectedSnapshot.EnsureComplete();
        var expectedItems = expectedSnapshot.Items;
        if (expectedItems.Count == 0)
            return 0;

        var isItem = expected is T;
        var expectedItem = isItem ? (T)(object)expected : default;
        var canMatchSubsequence = true;
        for (var start = 0; await actual.TryGetItem(start).ConfigureAwait(false) is (true, var item); start++)
        {
            if (isItem && comparer.Equals(expectedItem, item))
                return start;

            if (!canMatchSubsequence)
                continue;

            if (await actual.TryGetItem(start + expectedItems.Count - 1).ConfigureAwait(false) is not (true, _))
            {
                if (!isItem)
                    return -1;

                canMatchSubsequence = false;
                continue;
            }

            if (SubsequenceMatchesAt(expectedItems, actual.Items, start, comparer))
                return start;
        }

        return -1;
    }

    private static bool SubsequenceMatchesAt<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, int start, IEqualityComparer<T> comparer)
    {
        for (var i = 0; i < expected.Count; i++)
        {
            if (!comparer.Equals(expected[i], actual[start + i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc cref="IndexOfSubsequenceOrItem{T}(IEnumerable{T}, CollectionSnapshot{T}, CollectionSnapshot{T}, IEqualityComparer{T})"/>
    /// <remarks>
    /// A string or a custom comparer disables the item match: a string is never equal to a char, and a non-generic
    /// comparer may not accept a sequence as an argument.
    /// </remarks>
    private static int IndexOfSubsequenceOrItem(System.Collections.IEnumerable expected, CollectionSnapshot<object?> expectedSnapshot, CollectionSnapshot<object?> actual, System.Collections.IEqualityComparer? comparer)
    {
        expectedSnapshot.EnsureComplete();
        var expectedItems = expectedSnapshot.Items;
        if (expectedItems.Count == 0)
            return 0;

        var isItem = comparer is null && expected is not string;
        var canMatchSubsequence = true;
        for (var start = 0; actual.TryGetItem(start, out var item); start++)
        {
            if (isItem && object.Equals(expected, item))
                return start;

            if (!canMatchSubsequence)
                continue;

            if (!actual.TryGetItem(start + expectedItems.Count - 1, out _))
            {
                if (!isItem)
                    return -1;

                canMatchSubsequence = false;
                continue;
            }

            if (SubsequenceMatchesAt(expectedItems, actual, start, comparer))
                return start;
        }

        return -1;
    }

    private static bool SubsequenceMatchesAt(IReadOnlyList<object?> expected, CollectionSnapshot<object?> actual, int start, System.Collections.IEqualityComparer? comparer)
    {
        for (var i = 0; i < expected.Count; i++)
        {
            actual.TryGetItem(start + i, out var item);
            if (!Equals(expected[i], item, comparer))
                return false;
        }

        return true;
    }
}
