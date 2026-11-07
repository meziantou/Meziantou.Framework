using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// The overloads and their priorities mirror EndsWith, so both assertions bind the same call the same way.
public partial class Assert
{
    [OverloadResolutionPriority(1)]
    public static void DoesNotEndWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanExpectedActualValueAssertionError<T, T>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    // An array would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void DoesNotEndWith<T>(T expected, [NotNull] T[]? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(DoesNotEndWith), "Expected expression", "Not expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        DoesNotEndWith(expected, new ReadOnlySpan<T>(actual), comparer, message, actualExpression, expectedExpression);
    }

    // A string would otherwise bind to the ReadOnlySpan<char> overload, which turns a null string into an empty span.
    [OverloadResolutionPriority(1)]
    public static void DoesNotEndWith(char expected, [NotNull] string? actual, IEqualityComparer<char>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<char>(nameof(DoesNotEndWith), "Expected expression", "Not expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        DoesNotEndWith(expected, actual.AsSpan(), comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(1)]
    public static void DoesNotEndWith<T>(T expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<T>(nameof(DoesNotEndWith), "Expected expression", "Not expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        actualSnapshot.EnsureComplete();
        if (actualSnapshot.Items.Count is 0 || !comparer.Equals(expected, actualSnapshot.Items[^1]))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<T, IReadOnlyList<T>>("Not expected suffix", expected, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void DoesNotEndWith(object? expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<object?>(nameof(DoesNotEndWith), "Expected expression", "Not expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        // A string is never equal to a char, so comparing it to the last item of a char sequence could never fail.
        switch (expected, actual)
        {
            case (string expectedString, string actualString):
                DoesNotEndWith(expectedString, actualString, StringComparison.Ordinal, message, actualExpression, expectedExpression);
                return;

            case (string expectedString, IEnumerable<char>):
                DoesNotEndWith((System.Collections.IEnumerable)expectedString, actual, comparer: null, message, actualExpression, expectedExpression);
                return;
        }

        DoesNotEndWithValue(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    private static void DoesNotEndWithValue(object? expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        actualSnapshot.EnsureComplete();
        if (actualSnapshot.Items.Count is 0 || !Equals(expected, actualSnapshot.Items[^1], comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<object?, IReadOnlyList<object?>>("Not expected suffix", expected, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (GetFirstSuffixDifferenceIndex(expected, actual, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotEndWith<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(DoesNotEndWith), "Expected expression", "Not expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        if (GetFirstSuffixDifferenceIndex(expected, expectedSnapshot.Items, actualSnapshot.Items, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected suffix", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotEndWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    public static void DoesNotEndWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.EndsWith(expected, comparisonType))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<char, char>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotEndWith(string expected, [NotNull] string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        DoesNotEndWith(expected, actual, GetOrdinalComparison(ignoreCase), message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(2)]
    public static void DoesNotEndWith(string expected, [NotNull] string? actual, StringComparison comparisonType, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new StringNullActualAssertionError(nameof(DoesNotEndWith), "Not expected suffix", expected, comparisonType, actualExpression, expectedExpression, message)));
        }

        if (!actual.EndsWith(expected, comparisonType))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<string, string>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static async Task DoesNotEndWith<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(DoesNotEndWith), "Expected expression", "Not expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        expectedSnapshot.EnsureComplete();
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (GetFirstSuffixDifferenceIndex(expected, expectedSnapshot.Items, actualSnapshot.Items, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected suffix", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(DoesNotEndWith), "Expected expression", "Not expected suffix", expected, actualExpression, expectedExpression, message)));
        }

        // A string is itself an IEnumerable, so without this guard it binds here rather than to the object overload
        // and is searched as a char subsequence of a collection whose elements are not chars. That comparison can
        // never match, which makes the assertion impossible to fail.
        if (expected is string && actual is not IEnumerable<char>)
        {
            DoesNotEndWithValue(expected, actual, comparer, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        if (GetFirstSuffixDifferenceIndex(expected, expectedSnapshot.Items, actualSnapshot.Items, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<IReadOnlyList<object?>, IReadOnlyList<object?>>("Not expected suffix", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }
}
