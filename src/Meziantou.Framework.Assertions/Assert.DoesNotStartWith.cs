using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotStartWith<T>(T expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.IsEmpty || !comparer.Equals(expected, actual[0]))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanExpectedActualValueAssertionError<T, T>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith<T>(T expected, IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        if (!actualEnumerator.MoveNext() || !comparer.Equals(expected, actualEnumerator.Current))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<T, IEnumerable<T>>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith(object? expected, System.Collections.IEnumerable? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        if (!actualEnumerator.MoveNext() || !object.Equals(expected, actualEnumerator.Current))
        {
            return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<object?, System.Collections.IEnumerable>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (GetFirstDifferenceIndex(expected, actual, comparer) is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, StringComparison comparison = StringComparison.Ordinal, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!actual.StartsWith(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<char, char>(nameof(DoesNotStartWith), "Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith(string expected, string? actual, bool ignoreCase = false, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!actual.StartsWith(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<string, string>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null)));
    }

    public static async Task DoesNotStartWith<T>(IEnumerable<T> expected, IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        while (expectedEnumerator.MoveNext())
        {
            var actualHasNext = await actualEnumerator.MoveNextAsync().ConfigureAwait(false);
            if (!actualHasNext || !comparer.Equals(expectedEnumerator.Current, actualEnumerator.Current))
                return;
        }

        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected prefix", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message: null)));
    }

    public static void DoesNotStartWith(System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        while (expectedEnumerator.MoveNext())
        {
            var actualHasNext = actualEnumerator.MoveNext();
            if (!actualHasNext || !Equals(expectedEnumerator.Current, actualEnumerator.Current, comparer))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new DoesNotStartWithAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected prefix", expected, actual, actualExpression, expectedExpression, message: null)));
    }
}
