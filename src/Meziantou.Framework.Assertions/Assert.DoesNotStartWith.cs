using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotStartWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[0]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanExpectedActualValueAssertionError<T, T>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith<T>(T expected, IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!actualSnapshot.TryGetItem(0, out var item) || !comparer.Equals(expected, item))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<T, IEnumerable<T>>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith(object? expected, System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        if (!actualSnapshot.TryGetItem(0, out var item) || !object.Equals(expected, item))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<object?, System.Collections.IEnumerable>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (GetFirstDifferenceIndex(expected, actual, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!actual.StartsWith(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<char, char>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith(string expected, string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!actual.StartsWith(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<string, string>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static async Task DoesNotStartWith<T>(IEnumerable<T> expected, IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        for (var index = 0; expectedSnapshot.TryGetItem(index, out var expectedItem); index++)
        {
            var (actualHasNext, actualItem) = await actualSnapshot.TryGetItem(index).ConfigureAwait(false);
            if (!actualHasNext || !comparer.Equals(expectedItem, actualItem))
                return;
        }

        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected prefix", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotStartWith(System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        for (var index = 0; expectedSnapshot.TryGetItem(index, out var expectedItem); index++)
        {
            var actualHasNext = actualSnapshot.TryGetItem(index, out var actualItem);
            if (!actualHasNext || !Equals(expectedItem, actualItem, comparer))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message)));
    }
}
