using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotEndWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[^1]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanExpectedActualValueAssertionError<T, T>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith<T>(T expected, IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        actualSnapshot.EnsureComplete();
        if (actualSnapshot.Items.Count is 0 || !comparer.Equals(expected, actualSnapshot.Items[^1]))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<T, IEnumerable<T>>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith(object? expected, System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        actualSnapshot.EnsureComplete();
        if (actualSnapshot.Items.Count is 0 || !object.Equals(expected, actualSnapshot.Items[^1]))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<object?, System.Collections.IEnumerable>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (GetFirstSuffixDifferenceIndex(expected, actual, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!actual.EndsWith(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<char, char>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith(string expected, string? actual, bool ignoreCase = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!actual.EndsWith(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<string, string>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static async Task DoesNotEndWith<T>(IEnumerable<T> expected, IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        expectedSnapshot.EnsureComplete();
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected suffix", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void DoesNotEndWith(System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        if (GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotEndWithAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message)));
    }
}
