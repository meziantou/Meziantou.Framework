using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// The overloads and their priorities mirror Contains, so both assertions bind the same call the same way.
public partial class Assert
{
    private const string FoundItemIndexLabel = "Index of found item";
    private const string FoundKeyIndexLabel = "Index of found key";
    private const string FoundSubsequenceIndexLabel = "Index of found subsequence";
    private const string FoundSubstringIndexLabel = "Index of found substring";

    [OverloadResolutionPriority(1)]
    public static void DoesNotContain<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        for (var i = 0; i < actual.Length; i++)
        {
            if (!comparer.Equals(expected, actual[i]))
                continue;

            throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanDoesNotContainItemAssertionError<T>(expected, actual, i, actualExpression, expectedExpression, message)));
        }
    }

    // An array would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void DoesNotContain<T>(T expected, [NotNull] T[]? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(DoesNotContain), "Expected expression", "Not expected item", expected, actualExpression, expectedExpression, message)));
        }

        DoesNotContain(expected, new ReadOnlySpan<T>(actual), comparer, message, actualExpression, expectedExpression);
    }

    // A string would otherwise bind to the ReadOnlySpan<char> overload, which turns a null string into an empty span.
    [OverloadResolutionPriority(1)]
    public static void DoesNotContain(char expected, [NotNull] string? actual, IEqualityComparer<char>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<char>(nameof(DoesNotContain), "Expected expression", "Not expected item", expected, actualExpression, expectedExpression, message)));
        }

        DoesNotContain(expected, actual.AsSpan(), comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(1)]
    public static void DoesNotContain<T>(T expected, [NotNull] ICollection<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(DoesNotContain), "Expected expression", "Not expected item", expected, actualExpression, expectedExpression, message)));
        }

        if (!actual.Contains(expected))
            return;

        ThrowCollectionContainsItem(expected, actual, actualExpression, expectedExpression, message);
    }

    [OverloadResolutionPriority(1)]
    public static void DoesNotContain<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(DoesNotContain), "Expected expression", "Not expected item", expected, actualExpression, expectedExpression, message)));
        }

        // Like Enumerable.Contains, a collection decides with its own comparer (e.g. a case-insensitive set)
        if (comparer is null && actual is ICollection<T> collection)
        {
            if (!collection.Contains(expected))
                return;

            ThrowCollectionContainsItem(expected, collection, actualExpression, expectedExpression, message);
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            if (!comparer.Equals(expected, item))
                continue;

            throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<T, CollectionSnapshot<T>>("Not expected item", expected, actualSnapshot, actualExpression, expectedExpression, message, index, FoundItemIndexLabel)));
        }
    }

    [DoesNotReturn]
    private static void ThrowCollectionContainsItem<T>(T expected, ICollection<T> actual, string? actualExpression, string? expectedExpression, string? message)
    {
        // The collection may use another comparer, in which case the item cannot be located in the message.
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        int? foundIndex = null;
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            if (comparer.Equals(expected, item))
            {
                foundIndex = index;
                break;
            }
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<T, CollectionSnapshot<T>>("Not expected item", expected, actualSnapshot, actualExpression, expectedExpression, message, foundIndex, FoundItemIndexLabel)));
    }

    [OverloadResolutionPriority(1)]
    public static void DoesNotContain<T>([NotNull] IEnumerable<T>? actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new PredicateNullActualAssertionError(nameof(DoesNotContain), actualExpression, predicateExpression, message)));
        }

        using var matchingSnapshot = CollectionSnapshot.Create<T>(EnumerateMatchingItems(actual, predicate));
        if (!matchingSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionDoesNotContainPredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression, message)));
    }

    [OverloadResolutionPriority(1)]
    public static void DoesNotContain<TKey, TValue>(TKey expected, [NotNull] IEnumerable<KeyValuePair<TKey, TValue>>? actual, IEqualityComparer<TKey>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<TKey>(nameof(DoesNotContain), "Expected key expression", "Not expected key", expected, actualExpression, expectedExpression, message)));
        }

        using var actualSnapshot = CollectionSnapshot.Create<KeyValuePair<TKey, TValue>>(actual);
        if (!TryFindKey(expected, actual, actualSnapshot, comparer, out _, out var foundIndex))
            return;

        // A dictionary lookup does not tell where the key is. The default comparer finds it unless the dictionary uses another one.
        foundIndex ??= IndexOfKey(expected, actualSnapshot, EqualityComparer<TKey>.Default);
        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<TKey, CollectionSnapshot<KeyValuePair<TKey, TValue>>>("Not expected key", expected, actualSnapshot, actualExpression, expectedExpression, message, foundIndex, FoundKeyIndexLabel)));
    }

    private static int? IndexOfKey<TKey, TValue>(TKey expected, IEnumerable<KeyValuePair<TKey, TValue>> actual, IEqualityComparer<TKey> comparer)
    {
        var index = 0;
        foreach (var item in actual)
        {
            if (comparer.Equals(expected, item.Key))
                return index;

            index++;
        }

        return null;
    }

    [OverloadResolutionPriority(1)]
    public static void DoesNotContain<TKey, TValue>(TKey expected, [NotNull] Dictionary<TKey, TValue>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        where TKey : notnull
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<TKey>(nameof(DoesNotContain), "Expected key expression", "Not expected key", expected, actualExpression, expectedExpression, message)));
        }

        // A dictionary cannot contain a null key, and looking one up throws.
        if (expected is null || !actual.ContainsKey(expected))
            return;

        var foundIndex = IndexOfKey(expected, actual, actual.Comparer);
        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<TKey, Dictionary<TKey, TValue>>("Not expected key", expected, actual, actualExpression, expectedExpression, message, foundIndex, FoundKeyIndexLabel)));
    }

    [OverloadResolutionPriority(-1)]
    public static void DoesNotContain(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(DoesNotContain), "Expected expression", "Not expected", expected, actualExpression, expectedExpression, message)));
        }

        // A string is never equal to a char, so comparing it to each item of a char sequence could never fail.
        switch (expected, actual)
        {
            case (string expectedString, string actualString):
                DoesNotContain(expectedString, actualString, StringComparison.Ordinal, message, actualExpression, expectedExpression);
                return;

            case (string expectedString, IEnumerable<char>):
                DoesNotContain((System.Collections.IEnumerable)expectedString, actual, comparer: null, message, actualExpression, expectedExpression);
                return;
        }

        DoesNotContainValue(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(-1)]
    public static void DoesNotContain(object? expected, [NotNull] System.Collections.IDictionary? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(DoesNotContain), "Expected key expression", "Not expected key", expected, actualExpression, expectedExpression, message)));
        }

        // A dictionary cannot contain a null key, and looking one up throws.
        if (expected is null || !actual.Contains(expected))
            return;

        int? foundIndex = null;
        var index = 0;
        var enumerator = actual.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (object.Equals(expected, enumerator.Key))
            {
                foundIndex = index;
                break;
            }

            index++;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<object?, System.Collections.IDictionary>("Not expected key", expected, actual, actualExpression, expectedExpression, message, foundIndex, FoundKeyIndexLabel)));
    }

    public static void DoesNotContain(string expected, [NotNull] System.Collections.IDictionary? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotContain((object?)expected, actual, message, actualExpression, expectedExpression);
    }

    public static void DoesNotContain<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var foundIndex = IndexOfSubsequence(expected, actual, comparer);
        if (foundIndex < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanDoesNotContainAssertionError<T>("Not expected", expected, actual, foundIndex, FoundSubsequenceIndexLabel, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotContain<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(DoesNotContain), "Expected expression", "Not expected", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        var foundIndex = IndexOfSubsequenceOrItem(expected, expectedSnapshot, actualSnapshot, comparer);
        if (foundIndex < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<IReadOnlyList<T>, CollectionSnapshot<T>>("Not expected", expectedSnapshot.Items, actualSnapshot, actualExpression, expectedExpression, message, foundIndex, FoundSubsequenceIndexLabel)));
    }

    public static void DoesNotContain(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotContain(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    public static void DoesNotContain(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var foundIndex = actual.IndexOf(expected, comparisonType);
        if (foundIndex < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanDoesNotContainAssertionError<char>("Not expected", expected, actual, foundIndex, FoundSubstringIndexLabel, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotContain(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotContain(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotContain(string expected, [NotNull] string? actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(DoesNotContain), "Not expected", expected, comparisonType, actualExpression, expectedExpression, message)));
        }

        var foundIndex = actual.IndexOf(expected, comparisonType);
        if (foundIndex < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<string, string>("Not expected", expected, actual, actualExpression, expectedExpression, message, foundIndex, FoundSubstringIndexLabel)));
    }

    public static async Task DoesNotContain<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(DoesNotContain), "Expected expression", "Not expected", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        var foundIndex = await IndexOfSubsequenceOrItemAsync(expected, expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false);
        if (foundIndex < 0)
            return;

        var actualItems = await GetFormattedItemsAsync(actualSnapshot, foundIndex).ConfigureAwait(false);
        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualItems, actualExpression, expectedExpression, message, foundIndex, FoundSubsequenceIndexLabel)));
    }

    /// <summary>Observes the items a failure message highlighting <paramref name="highlightedIndex"/> can show, including the ones after it.</summary>
    private static async Task<IReadOnlyList<T>> GetFormattedItemsAsync<T>(AsyncCollectionSnapshot<T> snapshot, int highlightedIndex)
    {
        var items = await ErrorFormatter.GetFormattedItemsAsync(snapshot).ConfigureAwait(false);
        var maxIndex = highlightedIndex + Math.Max(ErrorFormatter.HighlightedContextItemCount, ErrorFormatter.SuffixItemCount) + 1;
        try
        {
            for (var index = snapshot.ObservedCount; index <= maxIndex; index++)
            {
                if (await snapshot.TryGetItem(index).ConfigureAwait(false) is not (true, _))
                    break;
            }
        }
        catch (Exception)
        {
            // Reading past the match is only for the message, so a failing sequence must not replace the assertion failure.
        }

        return items;
    }

    public static void DoesNotContain(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(DoesNotContain), "Expected expression", "Not expected", expected, actualExpression, expectedExpression, message)));
        }

        // A string is itself an IEnumerable, so without this guard it binds here rather than to the object overload
        // and is searched as a char subsequence of a collection whose elements are not chars. That comparison can
        // never match, which makes the assertion impossible to fail.
        if (expected is string && actual is not IEnumerable<char>)
        {
            DoesNotContainValue(expected, actual, comparer, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);
        var foundIndex = IndexOfSubsequenceOrItem(expected, expectedSnapshot, actualSnapshot, comparer);
        if (foundIndex < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<IReadOnlyList<object?>, CollectionSnapshot<object?>>("Not expected", expectedSnapshot.Items, actualSnapshot, actualExpression, expectedExpression, message, foundIndex, FoundSubsequenceIndexLabel)));
    }

    private static void DoesNotContainValue(object? expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            if (!Equals(expected, item, comparer))
                continue;

            throw new AssertionException(ErrorFormatter.Format(new DoesNotContainAssertionError<object?, CollectionSnapshot<object?>>("Not expected", expected, actualSnapshot, actualExpression, expectedExpression, message, index, FoundItemIndexLabel)));
        }
    }
}
