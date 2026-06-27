using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotEndWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[^1]))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanExpectedActualValueAssertionError<T, T>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith<T>(T expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count is 0 || !comparer.Equals(expected, actualSnapshot.Items[^1]))
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotEndWithAssertionError<T, IEnumerable<T>>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith(object? expected, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count is 0 || !object.Equals(expected, actualSnapshot.Items[^1]))
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotEndWithAssertionError<object?, System.Collections.IEnumerable>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (GetFirstSuffixDifferenceIndex(expected, actual, comparer) is not null)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.EndsWith(expected, comparison))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanValueAssertionError<char, char>(nameof(DoesNotEndWith), "Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith(string expected, string actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.EndsWith(expected, comparison))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotEndWithAssertionError<string, string>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static async Task DoesNotEndWith<T>(IEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);

        EnsureComplete(expectedSnapshot);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        if (GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not null)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotEndWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected suffix", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotEndWith(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));
        EnsureComplete(expectedSnapshot);
        EnsureComplete(actualSnapshot);
        if (GetFirstSuffixDifferenceIndex(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not null)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new DoesNotEndWithAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected suffix", expected, actual, actualExpression, expectedExpression, message: null)));
    }
}
