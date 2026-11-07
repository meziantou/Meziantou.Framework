using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// The overloads and their priorities mirror StartsWith, so both assertions bind the same call the same way.
public partial class Assert
{
    [OverloadResolutionPriority(1)]
    public static void DoesNotStartWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[0]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanExpectedActualValueAssertionError<T, T>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    // An array would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void DoesNotStartWith<T>(T expected, [NotNull] T[]? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(DoesNotStartWith), "Expected expression", "Not expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        DoesNotStartWith(expected, new ReadOnlySpan<T>(actual), comparer, message, actualExpression, expectedExpression);
    }

    // A string would otherwise bind to the ReadOnlySpan<char> overload, which turns a null string into an empty span.
    [OverloadResolutionPriority(1)]
    public static void DoesNotStartWith(char expected, [NotNull] string? actual, IEqualityComparer<char>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<char>(nameof(DoesNotStartWith), "Expected expression", "Not expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        DoesNotStartWith(expected, actual.AsSpan(), comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(1)]
    public static void DoesNotStartWith<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(DoesNotStartWith), "Expected expression", "Not expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!actualSnapshot.TryGetItem(0, out var item) || !comparer.Equals(expected, item))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<T, IReadOnlyList<T>>("Not expected prefix", expected, ErrorFormatter.GetFormattedItems(actualSnapshot), actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void DoesNotStartWith(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(DoesNotStartWith), "Expected expression", "Not expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        // A string is never equal to a char, so comparing it to the first item of a char sequence could never fail.
        switch (expected, actual)
        {
            case (string expectedString, string actualString):
                DoesNotStartWith(expectedString, actualString, StringComparison.Ordinal, message, actualExpression, expectedExpression);
                return;

            case (string expectedString, IEnumerable<char>):
                DoesNotStartWith((System.Collections.IEnumerable)expectedString, actual, comparer: null, message, actualExpression, expectedExpression);
                return;
        }

        DoesNotStartWithValue(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    private static void DoesNotStartWithValue(object? expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        if (!actualSnapshot.TryGetItem(0, out var item) || !Equals(expected, item, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<object?, IReadOnlyList<object?>>("Not expected prefix", expected, ErrorFormatter.GetFormattedItems(actualSnapshot), actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (GetFirstDifferenceIndex(expected, actual, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotStartWith<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(DoesNotStartWith), "Expected expression", "Not expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (GetFirstPrefixDifferenceIndex(expected, expectedSnapshot, actualSnapshot, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected prefix", ErrorFormatter.GetFormattedItems(expectedSnapshot), ErrorFormatter.GetFormattedItems(actualSnapshot), actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotStartWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    public static void DoesNotStartWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.StartsWith(expected, comparisonType))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<char, char>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotStartWith(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotStartWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotStartWith(string expected, [NotNull] string? actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(DoesNotStartWith), "Not expected prefix", expected, comparisonType, actualExpression, expectedExpression, message)));
        }

        if (!actual.StartsWith(expected, comparisonType))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<string, string>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static async Task DoesNotStartWith<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(DoesNotStartWith), "Expected expression", "Not expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        if (await GetFirstPrefixDifferenceIndexAsync(expected, expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false) is not null)
            return;

        var actualItems = await ErrorFormatter.GetFormattedItemsAsync(actualSnapshot).ConfigureAwait(false);
        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected prefix", ErrorFormatter.GetFormattedItems(expectedSnapshot), actualItems, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(DoesNotStartWith), "Expected expression", "Not expected prefix", expected, actualExpression, expectedExpression, message)));
        }

        // A string is itself an IEnumerable, so without this guard it binds here rather than to the object overload
        // and is searched as a char subsequence of a collection whose elements are not chars. That comparison can
        // never match, which makes the assertion impossible to fail.
        if (expected is string && actual is not IEnumerable<char>)
        {
            DoesNotStartWithValue(expected, actual, comparer, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        if (GetFirstPrefixDifferenceIndex(expected, expectedSnapshot, actualSnapshot, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<IReadOnlyList<object?>, IReadOnlyList<object?>>("Not expected prefix", ErrorFormatter.GetFormattedItems(expectedSnapshot), ErrorFormatter.GetFormattedItems(actualSnapshot), actualExpression, expectedExpression, message)));
    }
}
